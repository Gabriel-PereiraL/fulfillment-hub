using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Orders;

public sealed record CancelOrderCommand(Guid OrderId, string? Note);

/// <summary>
/// Cancels an order and returns its reserved stock in the same transaction. Customers can only cancel their own
/// orders (others look non-existent, D-18) and only while the rules for <c>CustomerRequest</c> allow it; operators
/// and administrators cancel with <c>OperatorAction</c>. An active delivery is cancelled at the provider first (the
/// provider refuses once the courier has the parcel → 409). Refunds are driven by the <c>OrderCancelled</c> event in
/// later phases (BL-244).
/// </summary>
public sealed partial class CancelOrderHandler(
    IFulfillmentHubDbContext db,
    ICurrentUser currentUser,
    IDeliveryProviderClient deliveryProvider,
    OrdersMetrics metrics,
    TimeProvider timeProvider,
    ILogger<CancelOrderHandler> logger)
{
    private static readonly Failure NotFound = Failure.NotFound("order.not_found", "Order not found.");

    public async Task<Result<OrderDto>> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(command.OrderId);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return NotFound;
        }

        var isOperator = currentUser.IsInRole(Role.Operator) || currentUser.IsInRole(Role.Admin);

        if (!isOperator && order.CustomerId != currentUser.CustomerId)
        {
            return NotFound;
        }

        var reason = isOperator ? OrderCancellationReason.OperatorAction : OrderCancellationReason.CustomerRequest;

        if (order.Status == OrderStatus.Cancelled)
        {
            return Result.Ok(OrderDto.From(order));
        }

        if (!order.CanCancel(reason))
        {
            return Failure.Conflict("order.cannot_cancel", $"An order in status '{order.Status}' can no longer be cancelled by you.");
        }

        var now = timeProvider.GetUtcNow();

        if (order.DeliveryId is { } deliveryId)
        {
            var delivery = await db.Deliveries.SingleAsync(d => d.Id == deliveryId, cancellationToken);

            if (delivery.IsActive && delivery.ProviderDeliveryId is { } providerDeliveryId)
            {
                var cancelled = await deliveryProvider.CancelAsync(providerDeliveryId, cancellationToken);

                if (!cancelled.IsSuccess)
                {
                    LogProviderCancelFailed(order.Id, cancelled.Failure.Code);
                    return cancelled.Failure.Code == "provider.noncancelable_delivery"
                        ? Failure.Conflict("order.delivery_in_progress", "The courier already has the parcel; the delivery can no longer be cancelled.")
                        : cancelled.Failure;
                }

                delivery.Cancel(now);
            }
        }

        order.Cancel(reason, now, currentUser.UserId, command.Note);

        await StockRelease.ReleaseAsync(db, order, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        metrics.OrderCancelled(reason.ToString());
        LogOrderCancelled(order.Id, reason);

        return Result.Ok(OrderDto.From(order));
    }

    [LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "Order {OrderId} cancelled ({Reason})")]
    private partial void LogOrderCancelled(OrderId orderId, OrderCancellationReason reason);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Warning, Message = "Provider refused to cancel the delivery of order {OrderId} ({Code})")]
    private partial void LogProviderCancelFailed(OrderId orderId, string code);
}
