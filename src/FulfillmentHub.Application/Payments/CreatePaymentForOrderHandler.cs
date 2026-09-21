using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Payments;

public sealed record CreatePaymentForOrderCommand(Guid OrderId);

/// <summary>
/// Creates the payment for a freshly placed order at the provider. Idempotent end to end: the payment aggregate is
/// unique per active order (partial index) and the provider receives a key derived from the order id, so a retry
/// after a timeout can never charge twice. A permanent provider failure cancels the order and returns its stock;
/// a transient one leaves the payment pending for reconciliation to retry.
/// Triggered by the <c>OrderPlaced</c> outbox message (Phase 8) and by payment reconciliation.
/// </summary>
public sealed partial class CreatePaymentForOrderHandler(
    IFulfillmentHubDbContext db,
    IPaymentGatewayClient gateway,
    PaymentStatusApplier statusApplier,
    TimeProvider timeProvider,
    ILogger<CreatePaymentForOrderHandler> logger)
{
    public async Task<Result<PaymentId>> HandleAsync(CreatePaymentForOrderCommand command, CancellationToken cancellationToken)
    {
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity("CreatePayment");
        activity?.SetTag("order.id", command.OrderId);

        var orderId = OrderId.From(command.OrderId);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Failure.NotFound("payment.order_not_found", "Order not found.");
        }

        if (order.Status != OrderStatus.Created)
        {
            return Failure.Conflict("payment.order_not_awaiting_payment", $"Order is '{order.Status}', not awaiting a payment to be created.");
        }

        var payment = await db.Payments
            .SingleOrDefaultAsync(p => p.OrderId == orderId && p.Status != PaymentStatus.Failed && p.Status != PaymentStatus.Cancelled, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (payment is null)
        {
            payment = Payment.Create(orderId, order.Total, gateway.ProviderName, now);
            db.Payments.Add(payment);
        }

        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == order.CustomerId).Select(c => c.Id).SingleAsync(cancellationToken);

        // An attempt left pending by a crash mid-call is closed as transient before trying again.
        foreach (var abandoned in payment.Attempts.Where(a => a.Outcome == PaymentAttemptOutcome.Pending))
        {
            payment.CompleteAttempt(abandoned.Id, PaymentAttemptOutcome.TransientFailure, null, "abandoned", now);
        }

        var attempt = payment.StartAttempt(now);
        await db.SaveChangesAsync(cancellationToken); // the attempt is on record even if the process dies mid-call

        var result = await gateway.CreateAsync(
            new GatewayCreatePayment(payment.ProviderIdempotencyKey, payment.Amount, order.Id.Value.ToString(), customer.Value.ToString()),
            cancellationToken);

        now = timeProvider.GetUtcNow();

        if (result.IsSuccess)
        {
            var created = result.Value;
            payment.CompleteAttempt(attempt.Id, PaymentAttemptOutcome.Succeeded, created.ProviderPaymentId, null, now);
            order.MarkAwaitingPayment(payment.Id, now);

            // Some providers answer already settled (or the simulator's settle delay is zero).
            if (created.Status is not PaymentStatus.Pending)
            {
                await statusApplier.ApplyAsync(payment, created.Status, created.UpdatedAt, created.FailureCode, now, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            LogPaymentCreated(payment.Id, order.Id, created.ProviderPaymentId);
            return Result.Ok(payment.Id);
        }

        var failure = result.Failure;

        if (failure.Kind == FailureKind.Unavailable)
        {
            payment.CompleteAttempt(attempt.Id, PaymentAttemptOutcome.TransientFailure, null, failure.Code, now);
            await db.SaveChangesAsync(cancellationToken);
            LogProviderUnavailable(payment.Id, order.Id, failure.Code);
            return failure;
        }

        payment.CompleteAttempt(attempt.Id, PaymentAttemptOutcome.PermanentFailure, null, failure.Code, now);
        await statusApplier.ApplyAsync(payment, PaymentStatus.Failed, now, failure.Code, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        LogProviderRejected(payment.Id, order.Id, failure.Code);
        return failure;
    }

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information, Message = "Payment {PaymentId} created at provider for order {OrderId} (provider id {ProviderPaymentId})")]
    private partial void LogPaymentCreated(PaymentId paymentId, OrderId orderId, string providerPaymentId);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Warning, Message = "Payment provider unavailable for payment {PaymentId} / order {OrderId} ({Code}); will retry via reconciliation")]
    private partial void LogProviderUnavailable(PaymentId paymentId, OrderId orderId, string code);

    [LoggerMessage(EventId = 5012, Level = LogLevel.Warning, Message = "Payment provider rejected payment {PaymentId} / order {OrderId} ({Code}); order cancelled")]
    private partial void LogProviderRejected(PaymentId paymentId, OrderId orderId, string code);
}
