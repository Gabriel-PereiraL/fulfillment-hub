using System.Net;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>Injects latency, random 500s and "hangs" before the real handler runs, according to <see cref="ChaosOptions"/>.</summary>
public sealed class ChaosFilter(IOptionsMonitor<ChaosOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var chaos = options.CurrentValue;
        var cancellationToken = context.HttpContext.RequestAborted;

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
