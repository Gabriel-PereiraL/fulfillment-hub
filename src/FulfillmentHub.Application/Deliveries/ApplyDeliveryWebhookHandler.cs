using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Deliveries;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Application.Deliveries;

public sealed record DeliveryWebhookCommand(string ProviderDeliveryId, ProviderDeliveryEvent Event);

/// <summary>
/// Applies an <c>event.delivery_status</c> webhook to the matching delivery. The provider's word is taken as is:
/// unlike payments (D-P5), a delivery status is not money, and the aggregate's ordering rules already bound the
/// damage of a forged or stale event (D-59).
/// </summary>
public sealed class ApplyDeliveryWebhookHandler(
    IFulfillmentHubDbContext db,
    IDeliveryProviderClient provider,
    DeliveryStatusApplier statusApplier,
    TimeProvider timeProvider)
{
    public async Task<Result<DeliveryEventDisposition>> HandleAsync(DeliveryWebhookCommand command, CancellationToken cancellationToken)
    {
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity("ApplyDeliveryWebhook");
        activity?.SetTag("delivery.provider_id", command.ProviderDeliveryId);

        var providerName = provider.ProviderName;
        var delivery = await db.Deliveries
            .SingleOrDefaultAsync(d => d.Provider == providerName && d.ProviderDeliveryId == command.ProviderDeliveryId, cancellationToken);

        if (delivery is null)
        {
            return Failure.NotFound("delivery.not_found", $"No delivery with provider id '{command.ProviderDeliveryId}'.");
        }

        var disposition = await statusApplier.ApplyAsync(delivery, command.Event, timeProvider.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Ok(disposition);
    }
}
