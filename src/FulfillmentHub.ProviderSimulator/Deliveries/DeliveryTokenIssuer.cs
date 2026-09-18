using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Deliveries;

/// <summary>
/// Fake OAuth2 client-credentials issuer: opaque bearer tokens with a short lifetime, so the client's token cache
/// and renewal paths are exercised. Not an OAuth implementation.
/// </summary>
public sealed class DeliveryTokenIssuer(IOptionsMonitor<DeliverySimulatorOptions> options, TimeProvider timeProvider)
{
    public const string Scope = "eats.deliveries";

    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new(StringComparer.Ordinal);

    public TokenResponse? Issue(string? clientId, string? clientSecret, string? grantType)
    {
        var settings = options.CurrentValue;

        if (grantType != "client_credentials"
            || !string.Equals(clientId, settings.ClientId, StringComparison.Ordinal)
            || !string.Equals(clientSecret, settings.ClientSecret, StringComparison.Ordinal))
        {
            return null;
        }

        var token = "sim_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        _tokens[token] = timeProvider.GetUtcNow().AddSeconds(settings.TokenLifetimeSeconds);
        Prune();

        return new TokenResponse(token, "Bearer", settings.TokenLifetimeSeconds, Scope);
    }

    public bool IsValid(string? token) =>
        token is not null && _tokens.TryGetValue(token, out var expires) && expires > timeProvider.GetUtcNow();

    /// <summary>Test/demo hook: invalidates every issued token (simulates a revocation on the provider side).</summary>
    public void RevokeAll() => _tokens.Clear();

    private void Prune()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (token, expires) in _tokens)
        {
            if (expires <= now)
            {
                _tokens.TryRemove(token, out _);
            }
        }
    }
}
