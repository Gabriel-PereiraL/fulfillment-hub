using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Deliveries;

public sealed class DeliveriesMetrics
{
    private readonly Counter<long> _quotes;
    private readonly Counter<long> _requested;
    private readonly Counter<long> _events;

    public DeliveriesMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);
        _quotes = meter.CreateCounter<long>("fh.deliveries.quotes", "{quote}", "Delivery quotes by outcome (quoted, fallback_fee, requoted).");
        _requested = meter.CreateCounter<long>("fh.deliveries.requested", "{delivery}", "Delivery creation attempts by outcome.");
        _events = meter.CreateCounter<long>("fh.deliveries.events", "{event}", "Provider delivery events by disposition (Applied, Duplicate, OutOfOrder, Stale, Conflict).");
    }

    public void Quote(string outcome) => _quotes.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Requested(string outcome) => _requested.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Event(string disposition) => _events.Add(1, new KeyValuePair<string, object?>("disposition", disposition));
}
