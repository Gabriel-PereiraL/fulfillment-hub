using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Providers;

/// <summary>Resilience knobs shared by every outbound provider client (docs/INTEGRATIONS.md §4).</summary>
public abstract class ProviderResilienceOptions
{
    [Range(1, 120)]
    public int TotalTimeoutSeconds { get; init; } = 15;

    [Range(1, 60)]
    public int AttemptTimeoutSeconds { get; init; } = 5;

    /// <summary>0 disables retries (useful in tests and for non-idempotent calls).</summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    [Range(10, 10_000)]
    public int RetryBaseDelayMs { get; init; } = 500;

    /// <summary>How long the circuit stays open before a half-open probe is allowed.</summary>
    [Range(1, 3600)]
    public int CircuitBreakDurationSeconds { get; init; } = 30;

    public TimeSpan TotalTimeout => TimeSpan.FromSeconds(TotalTimeoutSeconds);

    public TimeSpan AttemptTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds);

    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(RetryBaseDelayMs);

    public TimeSpan CircuitBreakDuration => TimeSpan.FromSeconds(CircuitBreakDurationSeconds);
}
