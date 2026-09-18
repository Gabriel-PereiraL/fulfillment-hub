using FulfillmentHub.Application.Common;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;

namespace FulfillmentHub.Application.Deliveries;

/// <summary>One end of a delivery (the store or the customer).</summary>
public sealed record DeliveryParty(string Name, Address Address, PhoneNumber Phone);

public sealed record GatewayQuoteRequest(DeliveryParty Pickup, DeliveryParty Dropoff, Money? ManifestTotalValue);

public sealed record GatewayQuote(
    string ProviderQuoteId,
    Money Fee,
    DateTimeOffset ExpiresAt,
    DateTimeOffset EstimatedDropoffAt,
    int DurationMinutes,
    int PickupDurationMinutes);

public sealed record GatewayManifestItem(string Name, int Quantity, Money UnitPrice);

public sealed record GatewayCreateDelivery(
    string IdempotencyKey,
    string? ProviderQuoteId,
    DeliveryParty Pickup,
    DeliveryParty Dropoff,
    IReadOnlyList<GatewayManifestItem> Items,
    string ManifestReference,
    Money ManifestTotalValue);

public sealed record GatewayDelivery(
    string ProviderDeliveryId,
    DeliveryStatus Status,
    string ProviderStatus,
    Money Fee,
    string? TrackingUrl,
    CourierInfo? Courier,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Port to the delivery provider (docs/INTEGRATIONS.md §1–§2). Implemented in Infrastructure against the simulator;
/// the same contract would front a real provider. Transient failures come back as <see cref="FailureKind.Unavailable"/>,
/// contract errors keep the provider's <c>code</c> in <see cref="Failure.Code"/> (<c>provider.&lt;code&gt;</c>).
/// </summary>
public interface IDeliveryProviderClient
{
    /// <summary>Metadata key on a <c>provider.duplicate_delivery</c> failure carrying the existing delivery id.</summary>
    const string DuplicateDeliveryIdKey = "delivery_id";

    string ProviderName { get; }

    Task<Result<GatewayQuote>> QuoteAsync(GatewayQuoteRequest request, CancellationToken cancellationToken);

    Task<Result<GatewayDelivery>> CreateAsync(GatewayCreateDelivery request, CancellationToken cancellationToken);

    Task<Result<GatewayDelivery>> GetAsync(string providerDeliveryId, CancellationToken cancellationToken);

    Task<Result<GatewayDelivery>> CancelAsync(string providerDeliveryId, CancellationToken cancellationToken);
}
