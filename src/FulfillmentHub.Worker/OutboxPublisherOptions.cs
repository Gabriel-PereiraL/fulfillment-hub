using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Worker;

/// <summary>How often the worker polls the outbox (section <c>Worker:Outbox</c>); the batch/retry knobs live in <c>Outbox</c>.</summary>
public sealed class OutboxPublisherOptions
{
    public const string SectionName = "Worker:Outbox";

    [Range(100, 60_000)]
    public int IntervalMs { get; init; } = 500;

    public TimeSpan Interval => TimeSpan.FromMilliseconds(IntervalMs);
}
