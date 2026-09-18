using System.Net;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>Endpoint metadata: which error code a simulated provider uses for 429 (they differ between providers).</summary>
public sealed record RateLimitErrorCode(string Code);

/// <summary>Fixed-window counter shared by every endpoint (endpoint filters are instantiated per endpoint).</summary>
public sealed class ChaosRateLimiter(TimeProvider timeProvider)
{
    private readonly Lock _windowLock = new();
    private long _windowStart;
    private int _windowCount;

    public bool IsOverLimit(int limitPerMinute, out int retryAfterSeconds)
    {
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var window = now / 60;

        lock (_windowLock)
        {
            if (window != _windowStart)
            {
                _windowStart = window;
                _windowCount = 0;
            }

            _windowCount++;
            retryAfterSeconds = (int)(60 - (now % 60));
            return _windowCount > limitPerMinute;
        }
    }
}

/// <summary>
/// Injects latency, random 500s, "hangs" and a fixed-window rate limit (429 + <c>Retry-After</c>) before the real
/// handler runs, according to <see cref="ChaosOptions"/>.
/// </summary>
public sealed class ChaosFilter(IOptionsMonitor<ChaosOptions> options, ChaosRateLimiter rateLimiter) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var chaos = options.CurrentValue;
        var cancellationToken = context.HttpContext.RequestAborted;

        if (chaos.RateLimitPerMinute > 0 && rateLimiter.IsOverLimit(chaos.RateLimitPerMinute, out var retryAfterSeconds))
        {
            var code = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RateLimitErrorCode>()?.Code ?? "rate_limited";
            context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return ProviderErrors.Problem(HttpStatusCode.TooManyRequests, code, "Too many requests; slow down.");
        }

        if (chaos.TimeoutRate > 0 && Random.Shared.NextDouble() < chaos.TimeoutRate)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
        }

        if (chaos.LatencyMs > 0 || chaos.LatencyJitterMs > 0)
        {
            await Task.Delay(chaos.LatencyMs + Random.Shared.Next(chaos.LatencyJitterMs + 1), cancellationToken);
        }

        if (chaos.FailureRate > 0 && Random.Shared.NextDouble() < chaos.FailureRate)
        {
            return ProviderErrors.Problem(HttpStatusCode.InternalServerError, "internal_server_error", "We have experienced a problem.");
        }

        return await next(context);
    }
}
