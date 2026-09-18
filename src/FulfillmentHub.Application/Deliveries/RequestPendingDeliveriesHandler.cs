using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Deliveries;

public sealed record DeliveryRequestSummary(int Candidates, int Requested, int Deferred);

/// <summary>
/// Finds paid orders without a delivery and requests one for each: the safety net behind the <c>OrderPaid</c> outbox
/// handler (D-68), so a parked message never leaves a paid order without a delivery. One order's failure never stops the batch.
/// </summary>
public sealed partial class RequestPendingDeliveriesHandler(
    IFulfillmentHubDbContext db,
    RequestDeliveryHandler requestDelivery,
    ILogger<RequestPendingDeliveriesHandler> logger)
{
    public async Task<DeliveryRequestSummary> HandleAsync(int batchSize, CancellationToken cancellationToken)
    {
        var candidates = await db.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid && o.DeliveryId == null)
            .OrderBy(o => o.UpdatedAt)
            .Take(batchSize)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var requested = 0;
        var deferred = 0;

        foreach (var orderId in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            db.ChangeTracker.Clear();

            try
            {
                var result = await requestDelivery.HandleAsync(new RequestDeliveryCommand(orderId.Value), cancellationToken);

                if (result.IsSuccess)
                {
                    requested++;
                }
                else
                {
                    deferred++;
                }
            }
            catch (Exception exception) when (exception is DbUpdateException or Domain.Common.DomainException)
            {
                deferred++;
                LogCandidateFailed(exception, orderId);
            }
        }

        return new DeliveryRequestSummary(candidates.Count, requested, deferred);
    }

    [LoggerMessage(EventId = 6020, Level = LogLevel.Error, Message = "Requesting the delivery for order {OrderId} failed; will retry on the next run")]
    private partial void LogCandidateFailed(Exception exception, OrderId orderId);
}
