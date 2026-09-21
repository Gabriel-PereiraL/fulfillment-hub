using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Domain.Orders;

namespace FulfillmentHub.Application.Orders;

/// <summary>Order metrics (docs/OBSERVABILITY.md §4). Singleton; the meter lifetime belongs to <see cref="IMeterFactory"/>.</summary>
public sealed class OrdersMetrics
{
    private readonly Counter<long> _placed;
    private readonly Counter<long> _cancelled;
    private readonly Counter<long> _reservationConflicts;
    private readonly Histogram<double> _timeToFinal;

    public OrdersMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);

        _placed = meter.CreateCounter<long>("fh.orders.placed", "{order}", "Orders successfully placed.");
        _cancelled = meter.CreateCounter<long>("fh.orders.cancelled", "{order}", "Orders cancelled, by reason.");
        _reservationConflicts = meter.CreateCounter<long>(
            "fh.stock.reservation_conflicts", "{conflict}", "Stock reservations rejected because of insufficient stock or a concurrent update.");
        _timeToFinal = meter.CreateHistogram<double>(
            "fh.order.time_to_final", "s", "Seconds from order creation to a final status (Delivered or Cancelled), by final status.");
    }

    public void OrderPlaced() => _placed.Add(1);

    public void OrderCancelled(string reason) => _cancelled.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void ReservationConflict(string kind) => _reservationConflicts.Add(1, new KeyValuePair<string, object?>("kind", kind));

    /// <summary>Call right after the order enters <see cref="OrderStatus.Delivered"/> or <see cref="OrderStatus.Cancelled"/>.</summary>
    public void OrderReachedFinalStatus(Order order, DateTimeOffset now) =>
        _timeToFinal.Record((now - order.CreatedAt).TotalSeconds, new KeyValuePair<string, object?>("final_status", order.Status.ToString()));
}
