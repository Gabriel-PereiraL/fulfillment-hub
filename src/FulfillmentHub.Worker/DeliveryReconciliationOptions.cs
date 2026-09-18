using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Worker;

public sealed class DeliveryReconciliationOptions
{
    public const string SectionName = "Worker:DeliveryReconciliation";

    [Range(5, 3600)]
    public int IntervalSeconds { get; init; } = 60;

    /// <summary>An active delivery without any provider event for this long is re-read from the provider.</summary>
    [Range(10, 86_400)]
    public int QuietForSeconds { get; init; } = 300;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    public TimeSpan QuietFor => TimeSpan.FromSeconds(QuietForSeconds);
}
