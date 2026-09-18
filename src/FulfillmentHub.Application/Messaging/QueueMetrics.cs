using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Messaging;

/// <summary>Queue consumer health (docs/OBSERVABILITY.md §4): throughput, age of what we consume and dead-letter depth.</summary>
public sealed class QueueMetrics
{
    private readonly Counter<long> _processed;
    private readonly Counter<long> _failed;
    private readonly Histogram<double> _age;
    private readonly Dictionary<string, long> _dlqDepth = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public QueueMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);
        _processed = meter.CreateCounter<long>("fh.queue.messages.processed", "{message}", "Messages consumed successfully.");
        _failed = meter.CreateCounter<long>("fh.queue.messages.failed", "{message}", "Messages whose handling failed (left for redelivery).");
        _age = meter.CreateHistogram<double>("fh.queue.message.age", "s", "Seconds between the message being sent and received.");
        meter.CreateObservableGauge("fh.queue.dlq.depth", ObserveDlqDepth, "{message}", "Approximate number of messages in each dead-letter queue (last poll).");
    }

    public void Processed(string queue, string consumer) => _processed.Add(1, Tag("queue", queue), Tag("consumer", consumer));

    public void Failed(string queue, string consumer, string reason) => _failed.Add(1, Tag("queue", queue), Tag("consumer", consumer), Tag("reason", reason));

    public void Age(string queue, TimeSpan age) => _age.Record(age.TotalSeconds, Tag("queue", queue));

    public void DlqDepth(string queue, long depth)
    {
        lock (_lock)
        {
            _dlqDepth[queue] = depth;
        }
    }

    private IEnumerable<Measurement<long>> ObserveDlqDepth()
    {
        lock (_lock)
        {
            return _dlqDepth.Select(pair => new Measurement<long>(pair.Value, Tag("queue", pair.Key))).ToList();
        }
    }

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
