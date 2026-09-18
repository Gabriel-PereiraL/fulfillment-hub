using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Domain.Orders.Events;

namespace FulfillmentHub.Application.Outbox.Handlers;

/// <summary><c>OrderPaid</c> → request the delivery (replaces the Phase 6 poll as the primary trigger, D-64).</summary>
public sealed class OrderPaidHandler(RequestDeliveryHandler requestDelivery) : OutboxHandler<OrderPaid>
{
    protected override async Task<OutboxHandling> HandleAsync(OrderPaid domainEvent, CancellationToken cancellationToken)
    {
        var result = await requestDelivery.HandleAsync(new RequestDeliveryCommand(domainEvent.OrderId.Value), cancellationToken);

        return result switch
        {
            { IsSuccess: true } => OutboxHandling.Completed,
            { Failure.Kind: FailureKind.Unavailable } => new OutboxHandling.Retry(result.Failure.Code),
            { Failure.Code: "delivery.quote_expired" } => new OutboxHandling.Retry(result.Failure.Code),
            // Permanent rejection already cancelled the order; "not paid" means the order moved on meanwhile.
            _ => OutboxHandling.Completed,
        };
    }
}
