using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
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
/// and administrators cancel with <c>OperatorAction</c>. Refund/provider cancellation are driven by the
/// <c>OrderCancelled</c> event in later phases.
/// </summary>
public sealed partial class CancelOrderHandler(
    IFulfillmentHubDbContext db,
    ICurrentUser currentUser,
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
        order.Cancel(reason, now, currentUser.UserId, command.Note);

        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);
        foreach (var item in order.Items)
        {
            products.Single(p => p.Id == item.ProductId).Release(item.Quantity, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        metrics.OrderCancelled(reason.ToString());
        LogOrderCancelled(order.Id, reason);

        return Result.Ok(OrderDto.From(order));
    }

    [LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "Order {OrderId} cancelled ({Reason})")]
    private partial void LogOrderCancelled(OrderId orderId, OrderCancellationReason reason);
}
