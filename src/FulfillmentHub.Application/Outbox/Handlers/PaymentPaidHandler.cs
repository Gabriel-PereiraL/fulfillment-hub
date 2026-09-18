using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Payments.Events;

namespace FulfillmentHub.Application.Outbox.Handlers;

/// <summary>
/// <c>PaymentPaid</c> → if the order was cancelled before the capture arrived (D-47 "late capture"), refund it.
/// For a live order nothing happens here: the order transition rides on the same webhook transaction.
/// </summary>
public sealed class PaymentPaidHandler(RefundPaymentHandler refund) : OutboxHandler<PaymentPaid>
{
    protected override async Task<OutboxHandling> HandleAsync(PaymentPaid domainEvent, CancellationToken cancellationToken)
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
