using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Orders;

/// <summary>Order metrics (docs/OBSERVABILITY.md §4). Singleton; the meter lifetime belongs to <see cref="IMeterFactory"/>.</summary>
public sealed class OrdersMetrics
{
    private readonly Counter<long> _placed;
    private readonly Counter<long> _cancelled;
    private readonly Counter<long> _reservationConflicts;

    public OrdersMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);

        _placed = meter.CreateCounter<long>("fh.orders.placed", "{order}", "Orders successfully placed.");
        _cancelled = meter.CreateCounter<long>("fh.orders.cancelled", "{order}", "Orders cancelled, by reason.");
        _reservationConflicts = meter.CreateCounter<long>(
            "fh.stock.reservation_conflicts", "{conflict}", "Stock reservations rejected because of insufficient stock or a concurrent update.");
    }

    public void OrderPlaced() => _placed.Add(1);

    public void OrderCancelled(string reason) => _cancelled.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void ReservationConflict(string kind) => _reservationConflicts.Add(1, new KeyValuePair<string, object?>("kind", kind));
}
