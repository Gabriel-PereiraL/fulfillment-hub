using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Outbox;

/// <summary>Outbox health (docs/OBSERVABILITY.md §4): throughput, lag and the two numbers worth alerting on.</summary>
public sealed class OutboxMetrics
{
    private readonly Counter<long> _published;
    private readonly Histogram<double> _lag;
    private readonly Histogram<double> _duration;
    private long _pending;
    private long _failed;

    public OutboxMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);
        _published = meter.CreateCounter<long>("fh.outbox.published", "{message}", "Outbox messages by outcome (processed, retried, failed).");
        _lag = meter.CreateHistogram<double>("fh.outbox.lag", "s", "Seconds between the event and its successful publication.");
        _duration = meter.CreateHistogram<double>("fh.outbox.publish.duration", "ms", "Handler duration per message.");
        meter.CreateObservableGauge("fh.outbox.pending", () => Interlocked.Read(ref _pending), "{message}", "Messages waiting to be published (last poll).");
        meter.CreateObservableGauge("fh.outbox.failed", () => Interlocked.Read(ref _failed), "{message}", "Messages that exhausted their attempts (last poll).");
    }

    public void Published(string outcome, string type) =>
        _published.Add(1, new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("type", type));

    public void Lag(TimeSpan lag, string type) => _lag.Record(lag.TotalSeconds, new KeyValuePair<string, object?>("type", type));

    public void Duration(TimeSpan duration, string type) => _duration.Record(duration.TotalMilliseconds, new KeyValuePair<string, object?>("type", type));

    public void Backlog(long pending, long failed)
    {
        Interlocked.Exchange(ref _pending, pending);
        Interlocked.Exchange(ref _failed, failed);
    }
}
