using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Deliveries;

public sealed record DeliveryReconciliationSummary(int Checked, int Corrected);

/// <summary>
/// Active deliveries that have been quiet for too long are re-read from the provider (docs/INTEGRATIONS.md §5, BL-067):
/// a lost webhook must not leave an order stuck. The provider's current status is applied as a synthetic event through
/// the same rules as webhooks, so reconciliation can never regress a delivery.
/// </summary>
public sealed partial class ReconcileDeliveriesHandler(
    IFulfillmentHubDbContext db,
    IDeliveryProviderClient provider,
    DeliveryStatusApplier statusApplier,
    PaymentsMetrics metrics,
    TimeProvider timeProvider,
    ILogger<ReconcileDeliveriesHandler> logger)
{
    public async Task<DeliveryReconciliationSummary> HandleAsync(TimeSpan quietFor, int batchSize, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cutoff = now - quietFor;
        var providerName = provider.ProviderName;
        var checkedCount = 0;
        var corrected = 0;

        var stale = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.Provider == providerName
                && d.ProviderDeliveryId != null
                && d.Status != DeliveryStatus.Requested
                && d.Status != DeliveryStatus.Delivered
                && d.Status != DeliveryStatus.Cancelled
                && d.Status != DeliveryStatus.Returned
                && d.UpdatedAt <= cutoff)
            .OrderBy(d => d.UpdatedAt)
            .Take(batchSize)
            .Select(d => new { d.Id, d.ProviderDeliveryId })
            .ToListAsync(cancellationToken);

        foreach (var candidate in stale)
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.ChangeTracker.Clear();

            try
            {
                checkedCount++;
                var remote = await provider.GetAsync(candidate.ProviderDeliveryId!, cancellationToken);

                if (!remote.IsSuccess)
                {
                    LogProviderReadFailed(candidate.Id, remote.Failure.Code);
                    continue;
                }

                var delivery = await db.Deliveries.SingleAsync(d => d.Id == candidate.Id, cancellationToken);
                var evt = new ProviderDeliveryEvent(
                    $"reconciled:{remote.Value.ProviderDeliveryId}:{remote.Value.ProviderStatus}:{remote.Value.UpdatedAt.ToUnixTimeSeconds()}",
                    remote.Value.ProviderStatus,
                    remote.Value.Status,
                    remote.Value.UpdatedAt,
                    remote.Value.Courier);

                var disposition = await statusApplier.ApplyAsync(delivery, evt, now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);

                if (disposition == DeliveryEventDisposition.Applied)
                {
                    corrected++;
                    metrics.ReconciliationCorrection("delivery_status");
                    LogCorrected(delivery.Id, delivery.Status);
                }
            }
            catch (Exception exception) when (exception is DbUpdateException or DomainException)
            {
                LogCandidateFailed(exception, candidate.Id);
            }
        }

        return new DeliveryReconciliationSummary(checkedCount, corrected);
    }

    [LoggerMessage(EventId = 6030, Level = LogLevel.Information, Message = "Reconciliation corrected delivery {DeliveryId} to {Status}")]
    private partial void LogCorrected(DeliveryId deliveryId, DeliveryStatus status);

    [LoggerMessage(EventId = 6031, Level = LogLevel.Warning, Message = "Reconciliation could not read delivery {DeliveryId} from the provider ({Code})")]
    private partial void LogProviderReadFailed(DeliveryId deliveryId, string code);

    [LoggerMessage(EventId = 6032, Level = LogLevel.Error, Message = "Reconciliation of delivery {DeliveryId} failed; will retry on the next run")]
    private partial void LogCandidateFailed(Exception exception, DeliveryId deliveryId);
}
