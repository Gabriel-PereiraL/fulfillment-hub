using System.Diagnostics.Metrics;
using FulfillmentHub.Infrastructure.Telemetry;

namespace FulfillmentHub.Worker;

/// <summary>
/// Metrics owned by the worker host. Registered as a singleton; the <see cref="Meter"/> lifetime is managed by
/// <see cref="IMeterFactory"/>.
/// </summary>
public sealed class WorkerMetrics
{
    private readonly Counter<long> _heartbeats;

    public WorkerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(TelemetryNames.Meter);

        _heartbeats = meter.CreateCounter<long>(
            "fh.worker.heartbeats",
            unit: "{heartbeat}",
            description: "Heartbeats emitted by the worker host; absence for several intervals means the worker is down.");
    }

    public void Heartbeat() => _heartbeats.Add(1);
}
