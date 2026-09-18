using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Worker;

public sealed class ReconciliationOptions
{
    public const string SectionName = "Worker:Reconciliation";

    [Range(5, 3600)]
    public int IntervalSeconds { get; init; } = 60;

    /// <summary>A payment must have been pending for at least this long before reconciliation touches it.</summary>
    [Range(10, 86_400)]
    public int PendingForSeconds { get; init; } = 120;

    [Range(1, 1000)]
    public int BatchSize { get; init; } = 50;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);

    public TimeSpan PendingFor => TimeSpan.FromSeconds(PendingForSeconds);
}
