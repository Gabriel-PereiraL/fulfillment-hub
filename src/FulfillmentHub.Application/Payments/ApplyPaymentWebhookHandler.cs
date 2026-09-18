using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Payments;

public sealed record PaymentWebhookCommand(
    string ProviderEventId,
    string ProviderPaymentId,
    PaymentStatus ReportedStatus,
    string? FailureCode,
    DateTimeOffset OccurredAt);

/// <summary>
/// Applies a <c>payment.status_changed</c> event. Deduplication happened before (webhook inbox); here the event
/// is matched to the payment and, for money-moving statuses, verified against the provider with a direct read
/// (D-P5: a webhook alone — even correctly signed — never confirms a payment).
/// </summary>
public sealed partial class ApplyPaymentWebhookHandler(
    IFulfillmentHubDbContext db,
    IPaymentGatewayClient gateway,
    PaymentStatusApplier statusApplier,
    TimeProvider timeProvider,
    ILogger<ApplyPaymentWebhookHandler> logger)
{
    public async Task<Result<bool>> HandleAsync(PaymentWebhookCommand command, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .SingleOrDefaultAsync(p => p.Provider == gateway.ProviderName && p.ProviderPaymentId == command.ProviderPaymentId, cancellationToken);

        if (payment is null)
        {
            LogUnknownPayment(command.ProviderEventId, command.ProviderPaymentId);
            return Failure.NotFound("webhook.unknown_payment", "No payment matches the provider payment id.");
        }

        var status = command.ReportedStatus;
        var failureCode = command.FailureCode;
        var occurredAt = command.OccurredAt;

        if (status is PaymentStatus.Paid or PaymentStatus.Refunded)
        {
            var verified = await gateway.GetAsync(command.ProviderPaymentId, cancellationToken);

            if (!verified.IsSuccess)
            {
                LogVerificationFailed(command.ProviderEventId, verified.Failure.Code);
                return verified.Failure;
            }

            status = verified.Value.Status;
            failureCode = verified.Value.FailureCode;
            occurredAt = verified.Value.UpdatedAt;
        }

        var changed = await statusApplier.ApplyAsync(payment, status, occurredAt, failureCode, timeProvider.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok(changed);
    }

    [LoggerMessage(EventId = 5020, Level = LogLevel.Warning, Message = "Webhook {ProviderEventId} references unknown provider payment {ProviderPaymentId}")]
    private partial void LogUnknownPayment(string providerEventId, string providerPaymentId);

    [LoggerMessage(EventId = 5021, Level = LogLevel.Warning, Message = "Could not verify webhook {ProviderEventId} against the provider ({Code}); will rely on reconciliation")]
    private partial void LogVerificationFailed(string providerEventId, string code);
}
