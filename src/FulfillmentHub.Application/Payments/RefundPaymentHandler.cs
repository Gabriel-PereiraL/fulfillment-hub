using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Payments;

public sealed record RefundOrderPaymentCommand(Guid OrderId);

/// <summary>
/// Refunds the paid payment of a cancelled order (BL-244): the customer's money goes back whenever an order is
/// cancelled after capture, whether the cancellation came first (customer/operator/delivery failure) or the capture
/// arrived late. Idempotent: a payment that is not <c>Paid</c> has nothing to refund; the provider call carries a
/// stable idempotency key per payment.
/// </summary>
public sealed partial class RefundPaymentHandler(
    IFulfillmentHubDbContext db,
    IPaymentGatewayClient gateway,
    PaymentsMetrics metrics,
    TimeProvider timeProvider,
    ILogger<RefundPaymentHandler> logger)
{
    /// <summary>True when a refund was issued now; false when there was nothing to refund.</summary>
    public async Task<Result<bool>> HandleAsync(RefundOrderPaymentCommand command, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(command.OrderId);
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Failure.NotFound("order.not_found", "Order not found.");
        }

        if (order.Status != OrderStatus.Cancelled)
        {
            return Result.Ok(false); // only cancelled orders give money back
        }

        var payment = await db.Payments
            .Where(p => p.OrderId == orderId && p.Status == PaymentStatus.Paid)
            .SingleOrDefaultAsync(cancellationToken);

        if (payment is null || payment.ProviderPaymentId is null)
        {
            return Result.Ok(false);
        }

        var refund = await gateway.RefundAsync(payment.ProviderPaymentId, $"refund-{payment.Id.Value:N}", cancellationToken);

        if (!refund.IsSuccess)
        {
            LogRefundFailed(payment.Id, order.Id, refund.Failure.Code);
            return refund.Failure;
        }

        var now = timeProvider.GetUtcNow();
        payment.ApplyProviderStatus(PaymentStatus.Refunded, now, failureReason: null, now);
        await db.SaveChangesAsync(cancellationToken);

        metrics.PaymentSettled("refunded");
        LogRefunded(payment.Id, order.Id, refund.Value.RefundId);
        return Result.Ok(true);
    }

    [LoggerMessage(EventId = 5040, Level = LogLevel.Information, Message = "Payment {PaymentId} of cancelled order {OrderId} refunded ({ProviderRefundId})")]
    private partial void LogRefunded(PaymentId paymentId, OrderId orderId, string providerRefundId);

    [LoggerMessage(EventId = 5041, Level = LogLevel.Warning, Message = "Refund of payment {PaymentId} for order {OrderId} failed ({Code}); will be retried")]
    private partial void LogRefundFailed(PaymentId paymentId, OrderId orderId, string code);
}
