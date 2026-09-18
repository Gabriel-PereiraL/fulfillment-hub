using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>
/// Failure injection shared by every simulated provider (docs/INTEGRATIONS.md §2.3). All values are safe defaults;
/// tests and demos override them through configuration (<c>Simulator:Chaos:*</c>).
/// </summary>
public sealed class ChaosOptions
{
    public const string SectionName = "Simulator:Chaos";

    [Range(0, 60_000)]
    public int LatencyMs { get; init; }

    [Range(0, 60_000)]
    public int LatencyJitterMs { get; init; }

    /// <summary>Fraction (0–1) of requests answered with HTTP 500.</summary>
    [Range(0, 1)]
    public double FailureRate { get; init; }

    /// <summary>Fraction (0–1) of requests that hang until the caller's timeout fires.</summary>
    [Range(0, 1)]
    public double TimeoutRate { get; init; }

    /// <summary>Requests per minute per client before answering 429; 0 disables.</summary>
    [Range(0, 100_000)]
    public int RateLimitPerMinute { get; init; }
}
