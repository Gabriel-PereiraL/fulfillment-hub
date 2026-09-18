using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Payments;

public sealed record ReconciliationSummary(int Checked, int Corrected, int Retried);

/// <summary>
/// Safety net for lost or delayed webhooks and interrupted provider calls (docs/INTEGRATIONS.md §5, T18):
/// payments pending/authorized for longer than the threshold are re-read from the provider; payments the provider
/// never acknowledged (no provider id) are created again with the same idempotency key.
/// Each payment is its own unit of work so one failure does not block the batch.
/// </summary>
public sealed partial class ReconcilePaymentsHandler(
    IFulfillmentHubDbContext db,
    IPaymentGatewayClient gateway,
    PaymentStatusApplier statusApplier,
    CreatePaymentForOrderHandler createPayment,
    PaymentsMetrics metrics,
    TimeProvider timeProvider,
    ILogger<ReconcilePaymentsHandler> logger)
{
    public async Task<ReconciliationSummary> HandleAsync(TimeSpan pendingFor, int batchSize, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now - pendingFor;
        var checkedCount = 0;
        var corrected = 0;
        var retried = 0;

        var stale = await db.Payments
            .AsNoTracking()
            .Where(p => (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Authorized) && p.UpdatedAt <= cutoff)
            .OrderBy(p => p.UpdatedAt)
            .Take(batchSize)
            .Select(p => new { p.Id, p.OrderId, p.ProviderPaymentId })
            .ToListAsync(cancellationToken);

        foreach (var candidate in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.ChangeTracker.Clear();

            try
            {
                if (candidate.ProviderPaymentId is null)
                {
                    var result = await createPayment.HandleAsync(new CreatePaymentForOrderCommand(candidate.OrderId.Value), cancellationToken);
                    retried++;
                    LogRetriedCreation(candidate.Id, result.IsSuccess ? "succeeded" : result.Failure.Code);
                    continue;
                }

                checkedCount++;
                var remote = await gateway.GetAsync(candidate.ProviderPaymentId, cancellationToken);

                if (!remote.IsSuccess)
                {
                    LogProviderReadFailed(candidate.Id, remote.Failure.Code);
                    continue;
                }

                var payment = await db.Payments.SingleAsync(p => p.Id == candidate.Id, cancellationToken);
                var changed = await statusApplier.ApplyAsync(payment, remote.Value.Status, remote.Value.UpdatedAt, remote.Value.FailureCode, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                if (changed)
                {
                    corrected++;
                    metrics.ReconciliationCorrection("payment_status");
                    LogCorrected(payment.Id, payment.Status);
                }
            }
            catch (Exception exception) when (exception is DbUpdateException or DomainException)
            {
                // One bad payment must not stop the batch; it stays stale and is retried on the next run.
                LogCandidateFailed(exception, candidate.Id);
            }
        }

        return new ReconciliationSummary(checkedCount, corrected, retried);
    }

    [LoggerMessage(EventId = 5030, Level = LogLevel.Information, Message = "Reconciliation corrected payment {PaymentId} to {Status}")]
    private partial void LogCorrected(PaymentId paymentId, PaymentStatus status);

    [LoggerMessage(EventId = 5033, Level = LogLevel.Error, Message = "Reconciliation of payment {PaymentId} failed; will retry on the next run")]
    private partial void LogCandidateFailed(Exception exception, PaymentId paymentId);

    [LoggerMessage(EventId = 5031, Level = LogLevel.Warning, Message = "Reconciliation could not read payment {PaymentId} from the provider ({Code})")]
    private partial void LogProviderReadFailed(PaymentId paymentId, string code);

    [LoggerMessage(EventId = 5032, Level = LogLevel.Information, Message = "Reconciliation retried provider creation for payment {PaymentId}: {Outcome}")]
    private partial void LogRetriedCreation(PaymentId paymentId, string outcome);
}
