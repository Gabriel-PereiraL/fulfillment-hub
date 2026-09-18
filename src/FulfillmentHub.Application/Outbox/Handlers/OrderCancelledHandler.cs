using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Orders.Events;

namespace FulfillmentHub.Application.Outbox.Handlers;

/// <summary><c>OrderCancelled</c> → refund the payment if it was captured (BL-244). Stock is released in the cancelling transaction itself.</summary>
public sealed class OrderCancelledHandler(RefundPaymentHandler refund) : OutboxHandler<OrderCancelled>
{
    protected override async Task<OutboxHandling> HandleAsync(OrderCancelled domainEvent, CancellationToken cancellationToken)
    {
        var result = await refund.HandleAsync(new RefundOrderPaymentCommand(domainEvent.OrderId.Value), cancellationToken);

        return result switch
        {
            { IsSuccess: true } => OutboxHandling.Completed,
            { Failure.Kind: FailureKind.Unavailable } => new OutboxHandling.Retry(result.Failure.Code),
            _ => OutboxHandling.Completed,
        };
    }
}
