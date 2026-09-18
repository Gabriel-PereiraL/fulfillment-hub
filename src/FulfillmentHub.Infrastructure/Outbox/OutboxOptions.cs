using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Outbox;

/// <summary>Publisher behaviour (section <c>Outbox</c>): batch, retry budget, backoff and lease.</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>Attempts before a message is parked as Failed (logical dead letter).</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    [Range(1, 3600)]
    public int BaseDelaySeconds { get; init; } = 2;

    [Range(1, 86_400)]
    public int MaxDelaySeconds { get; init; } = 300;

    /// <summary>How long a claimed message stays invisible to other publishers; must exceed the slowest handler.</summary>
    [Range(5, 3600)]
    public int LeaseSeconds { get; init; } = 60;

    public TimeSpan BaseDelay => TimeSpan.FromSeconds(BaseDelaySeconds);

    public TimeSpan MaxDelay => TimeSpan.FromSeconds(MaxDelaySeconds);

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);
}
