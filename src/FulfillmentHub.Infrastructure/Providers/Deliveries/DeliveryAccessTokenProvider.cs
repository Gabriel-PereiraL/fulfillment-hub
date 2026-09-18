using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

/// <summary>
/// Caches the provider's client-credentials token and renews it ahead of expiry (docs/INTEGRATIONS.md §4 "Token").
/// One token for the process; concurrent callers wait for a single renewal. Tokens are never logged.
/// </summary>
public sealed partial class DeliveryAccessTokenProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<DeliveryProviderOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryAccessTokenProvider> logger) : IDisposable
{
    public const string HttpClientName = "delivery-provider-auth";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly SemaphoreSlim _renewal = new(1, 1);
    private readonly Lock _cacheLock = new();
    private string? _token;
    private DateTimeOffset _expiresAt;

    /// <summary>Returns a token valid for at least the refresh skew, renewing it when needed.</summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (TryGetCached(out var cached))
        {
            return cached;
        }

        await _renewal.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(out cached))
            {
                return cached;
            }

            return await RenewAsync(cancellationToken);
        }
        finally
        {
            _renewal.Release();
        }
    }

    /// <summary>The provider rejected <paramref name="rejectedToken"/>: drop it so the next call renews (at most once per token).</summary>
    public void Invalidate(string rejectedToken)
    {
        lock (_cacheLock)
        {
            if (_token == rejectedToken)
            {
                _token = null;
            }
        }
    }

    private bool TryGetCached(out string token)
    {
        lock (_cacheLock)
        {
            if (_token is not null && _expiresAt - options.Value.TokenRefreshSkew > timeProvider.GetUtcNow())
            {
                token = _token;
                return true;
            }
        }

        token = null!;
        return false;
    }

    private async Task<string> RenewAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var client = httpClientFactory.CreateClient(HttpClientName);

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["client_secret"] = settings.ClientSecret,
            ["grant_type"] = "client_credentials",
            ["scope"] = "eats.deliveries",
        });

        using var response = await client.PostAsync("delivery/oauth/token", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            LogTokenRefused((int)response.StatusCode);
            throw new HttpRequestException($"Delivery provider refused the credentials (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
        }

        var token = await response.Content.ReadFromJsonAsync<ProviderTokenResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Empty token response.");

        lock (_cacheLock)
        {
            _token = token.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn);
        }

        LogTokenRenewed(token.ExpiresIn);
        return token.AccessToken;
    }

    public void Dispose() => _renewal.Dispose();

    [LoggerMessage(EventId = 6100, Level = LogLevel.Information, Message = "Delivery provider token renewed (expires in {ExpiresIn}s)")]
    private partial void LogTokenRenewed(int expiresIn);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Error, Message = "Delivery provider refused the client credentials (HTTP {StatusCode})")]
    private partial void LogTokenRefused(int statusCode);
}
