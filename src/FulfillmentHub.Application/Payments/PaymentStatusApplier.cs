using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Payments;

/// <summary>
/// The single place where a provider-reported payment status is applied to our aggregates: the payment itself,
/// the order (paid / cancelled with stock returned). Used by webhooks and by reconciliation, so both paths agree.
/// Does not call <c>SaveChangesAsync</c>: the caller owns the transaction.
/// </summary>
public sealed partial class PaymentStatusApplier(IFulfillmentHubDbContext db, PaymentsMetrics metrics, ILogger<PaymentStatusApplier> logger)
{
    /// <summary>Returns true when the reported status changed something.</summary>
    public async Task<bool> ApplyAsync(
        Payment payment,
        PaymentStatus reported,
        DateTimeOffset providerEventAt,
        string? failureCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!payment.ApplyProviderStatus(reported, providerEventAt, failureCode, now))
        {
            return false;
        }

        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == payment.OrderId, cancellationToken);

        if (order is null)
        {
            LogOrderMissing(payment.Id, payment.OrderId);
            return true;
        }

        switch (payment.Status)
        {
            case PaymentStatus.Paid when order.Status == OrderStatus.Cancelled:
                // The customer cancelled while the payment was pending and the provider captured it anyway.
                // The order stays cancelled; the money must go back (BL-244: automatic refund, Phase 8).
                metrics.PaymentSettled("paid_after_cancellation");
                LogLateCapture(payment.Id, order.Id);
                break;

            case PaymentStatus.Paid when order.Status is OrderStatus.Created or OrderStatus.AwaitingPayment:
                if (order.Status == OrderStatus.Created)
                {
                    // The webhook overtook our own "payment created" transaction: catch the order up first.
                    order.MarkAwaitingPayment(payment.Id, now);
                }

                order.MarkAsPaid(payment.Id, now);
                metrics.PaymentSettled("paid");
                LogPaymentApplied(payment.Id, order.Id, payment.Status);
                break;

            case PaymentStatus.Paid:
                // Already paid (or further along): the payment record catches up, the order is left alone.
                metrics.PaymentSettled("paid");
                LogPaymentApplied(payment.Id, order.Id, payment.Status);
                break;

            case PaymentStatus.Failed:
                if (order.CanCancel(OrderCancellationReason.PaymentFailed))
                {
                    order.Cancel(OrderCancellationReason.PaymentFailed, now, actor: null, note: failureCode);
                    await ReleaseStockAsync(order, now, cancellationToken);
                }

                metrics.PaymentSettled("failed");
                LogPaymentApplied(payment.Id, order.Id, payment.Status);
                break;

            case PaymentStatus.Cancelled:
            case PaymentStatus.Refunded:
            case PaymentStatus.Authorized:
            case PaymentStatus.Pending:
                LogPaymentApplied(payment.Id, order.Id, payment.Status);
                break;
        }

        return true;
    }

    private async Task ReleaseStockAsync(Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            products.Single(p => p.Id == item.ProductId).Release(item.Quantity, now);
        }
    }

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Payment {PaymentId} for order {OrderId} is now {Status}")]
    private partial void LogPaymentApplied(PaymentId paymentId, OrderId orderId, PaymentStatus status);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Error, Message = "Payment {PaymentId} references missing order {OrderId}")]
    private partial void LogOrderMissing(PaymentId paymentId, OrderId orderId);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Warning, Message = "Payment {PaymentId} was captured after order {OrderId} was cancelled; refund required")]
    private partial void LogLateCapture(PaymentId paymentId, OrderId orderId);
}
