using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Worker;

public sealed class DeliveryRequestOptions
{
    public const string SectionName = "Worker:DeliveryRequests";

    [Range(1, 3600)]
    public int IntervalSeconds { get; init; } = 10;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
