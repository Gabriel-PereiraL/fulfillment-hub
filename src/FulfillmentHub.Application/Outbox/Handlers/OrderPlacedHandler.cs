using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Orders.Events;

namespace FulfillmentHub.Application.Outbox.Handlers;

/// <summary><c>OrderPlaced</c> → create the payment at the provider. Idempotent through the payment's provider idempotency key.</summary>
public sealed class OrderPlacedHandler(CreatePaymentForOrderHandler createPayment) : OutboxHandler<OrderPlaced>
{
    protected override async Task<OutboxHandling> HandleAsync(OrderPlaced domainEvent, CancellationToken cancellationToken)
    {
        var result = await createPayment.HandleAsync(new CreatePaymentForOrderCommand(domainEvent.OrderId.Value), cancellationToken);

        return result switch
        {
            { IsSuccess: true } => OutboxHandling.Completed,
            { Failure.Kind: FailureKind.Unavailable } => new OutboxHandling.Retry(result.Failure.Code),
            // Order gone or no longer waiting for a payment (cancelled meanwhile, already paid): nothing left to do.
            _ => OutboxHandling.Completed,
        };
    }
}
