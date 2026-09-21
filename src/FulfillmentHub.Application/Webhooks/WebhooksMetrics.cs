using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Webhooks;

/// <summary>Counters shared by every inbound webhook endpoint (docs/OBSERVABILITY.md §4).</summary>
public sealed class WebhooksMetrics
{
    private readonly Counter<long> _received;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _duplicates;
    private readonly Counter<long> _outOfOrder;
    private readonly Histogram<double> _processingDuration;

    public WebhooksMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);
        _received = meter.CreateCounter<long>("fh.webhooks.received", "{webhook}", "Webhooks accepted (valid signature).");
        _rejected = meter.CreateCounter<long>("fh.webhooks.rejected", "{webhook}", "Webhooks rejected (bad signature, stale timestamp, malformed).");
        _duplicates = meter.CreateCounter<long>("fh.webhooks.duplicates", "{webhook}", "Webhooks already seen (same provider event id).");
        _outOfOrder = meter.CreateCounter<long>("fh.webhooks.out_of_order", "{webhook}", "Webhooks that arrived late or out of sequence (recorded, not applied).");
        _processingDuration = meter.CreateHistogram<double>(
            "fh.webhooks.processing.duration", "ms", "Time spent applying a stored webhook event (provider processor + inbox update).");
    }

    public void Received(string provider, string eventType) => _received.Add(1, Tag("provider", provider), Tag("event_type", eventType));

    public void Rejected(string provider, string reason) => _rejected.Add(1, Tag("provider", provider), Tag("reason", reason));

    public void Duplicate(string provider) => _duplicates.Add(1, Tag("provider", provider));

    public void OutOfOrder(string provider, string disposition) => _outOfOrder.Add(1, Tag("provider", provider), Tag("disposition", disposition));

    public void Processed(string provider, string outcome, TimeSpan elapsed) =>
        _processingDuration.Record(elapsed.TotalMilliseconds, Tag("provider", provider), Tag("outcome", outcome));

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
