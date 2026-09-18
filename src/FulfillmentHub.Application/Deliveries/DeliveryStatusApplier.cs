using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Deliveries;

/// <summary>A provider status report for a delivery, from a webhook or from reconciliation.</summary>
public sealed record ProviderDeliveryEvent(
    string ProviderEventId,
    string ProviderStatus,
    DeliveryStatus Status,
    DateTimeOffset OccurredAt,
    CourierInfo? Courier);

/// <summary>
/// The single place where a provider-reported delivery status is applied: the <see cref="Delivery"/> decides whether
/// the event moves it forward (DOMAIN.md §7: duplicates, stale and out-of-order events are recorded, not applied) and,
/// only when it does, the order follows: <c>pickup_complete → InDelivery</c>, <c>delivered → Delivered</c>,
/// <c>canceled/returned → Cancelled(DeliveryFailed)</c> with stock returned. Does not call <c>SaveChangesAsync</c>.
/// </summary>
public sealed partial class DeliveryStatusApplier(
    IFulfillmentHubDbContext db,
    DeliveriesMetrics metrics,
    WebhooksMetrics webhooksMetrics,
    ILogger<DeliveryStatusApplier> logger)
{
    public async Task<DeliveryEventDisposition> ApplyAsync(Delivery delivery, ProviderDeliveryEvent evt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var disposition = delivery.ApplyProviderEvent(evt.ProviderEventId, evt.ProviderStatus, evt.Status, evt.OccurredAt, evt.Courier, now);
        metrics.Event(disposition.ToString());

        if (disposition is DeliveryEventDisposition.OutOfOrder or DeliveryEventDisposition.Stale)
        {
            webhooksMetrics.OutOfOrder(delivery.Provider, disposition.ToString());
        }

        if (disposition != DeliveryEventDisposition.Applied)
        {
            LogNotApplied(delivery.Id, evt.ProviderStatus, disposition);
            return disposition;
        }

        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == delivery.OrderId, cancellationToken);
        if (order is null)
        {
            LogOrderMissing(delivery.Id, delivery.OrderId);
            return disposition;
        }

        switch (delivery.Status)
        {
            case DeliveryStatus.PickupComplete or DeliveryStatus.Dropoff when order.Status == OrderStatus.DeliveryRequested:
                order.MarkInDelivery(now);
                break;

            case DeliveryStatus.Delivered when order.Status is OrderStatus.DeliveryRequested or OrderStatus.InDelivery:
                if (order.Status == OrderStatus.DeliveryRequested)
                {
                    order.MarkInDelivery(now); // the pickup event was lost or is still on its way
                }

                order.MarkDelivered(now);
                break;

            case DeliveryStatus.Cancelled or DeliveryStatus.Returned when order.Status != OrderStatus.Cancelled && order.CanCancel(OrderCancellationReason.DeliveryFailed):
                order.Cancel(OrderCancellationReason.DeliveryFailed, now, actor: null, note: evt.ProviderStatus);
                await StockRelease.ReleaseAsync(db, order, now, cancellationToken);
                LogDeliveryFailed(delivery.Id, order.Id, evt.ProviderStatus);
                break;

            default:
                break; // pending/pickup, or the order already moved on (e.g. cancelled by the customer)
        }

        LogApplied(delivery.Id, order.Id, delivery.Status, order.Status);
        return disposition;
    }

    [LoggerMessage(EventId = 6200, Level = LogLevel.Information, Message = "Delivery {DeliveryId} is now {DeliveryStatus}; order {OrderId} is {OrderStatus}")]
    private partial void LogApplied(DeliveryId deliveryId, OrderId orderId, DeliveryStatus deliveryStatus, OrderStatus orderStatus);

    [LoggerMessage(EventId = 6201, Level = LogLevel.Information, Message = "Delivery {DeliveryId} event '{ProviderStatus}' recorded as {Disposition}")]
    private partial void LogNotApplied(DeliveryId deliveryId, string providerStatus, DeliveryEventDisposition disposition);

    [LoggerMessage(EventId = 6202, Level = LogLevel.Warning, Message = "Delivery {DeliveryId} ended as '{ProviderStatus}'; order {OrderId} cancelled, refund required (BL-244)")]
    private partial void LogDeliveryFailed(DeliveryId deliveryId, OrderId orderId, string providerStatus);

    [LoggerMessage(EventId = 6203, Level = LogLevel.Error, Message = "Delivery {DeliveryId} references missing order {OrderId}")]
    private partial void LogOrderMissing(DeliveryId deliveryId, OrderId orderId);
}
