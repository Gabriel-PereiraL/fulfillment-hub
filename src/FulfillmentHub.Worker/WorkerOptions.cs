using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    [Range(1, 300)]
    public int HeartbeatIntervalSeconds { get; init; } = 15;
}
