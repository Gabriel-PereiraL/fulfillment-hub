namespace FulfillmentHub.ProviderSimulator.Deliveries;

public sealed class SimulatedQuote
{
    public required string Id { get; init; }

    public required DateTimeOffset Created { get; init; }

    public required DateTimeOffset Expires { get; init; }

    public required long Fee { get; init; }

    public required string DropoffZip { get; init; }

    public required int DurationMinutes { get; init; }

    public required int PickupDurationMinutes { get; init; }

    public bool Used { get; set; }

    public DeliveryQuoteResponse ToResponse() => new(
        "delivery_quote",
        Id,
        Created,
        Expires,
        Fee,
        "brl",
        "BRL",
        Created.AddMinutes(DurationMinutes),
        DurationMinutes,
        PickupDurationMinutes,
        Created.AddMinutes(DurationMinutes + 30));
}

/// <summary>Mutable provider-side delivery; callers lock the instance when changing status.</summary>
public sealed class SimulatedDelivery
{
    public static readonly string[] Lifecycle = ["pending", "pickup", "pickup_complete", "dropoff", "delivered"];

    public required string Id { get; init; }

    public required string Uuid { get; init; }

    public string? QuoteId { get; init; }

    public required long Fee { get; init; }

    public required DateTimeOffset Created { get; init; }

    public DateTimeOffset Updated { get; set; }

    public string? ExternalId { get; init; }

    public string? ManifestReference { get; init; }

    public required string DropoffZip { get; init; }

    public string Status { get; set; } = "pending";

    public bool Complete => Status is "delivered" or "canceled" or "returned";

    public CourierResponse? Courier { get; set; }

    public DateTimeOffset? NextTransitionAt { get; set; }

    public DateTimeOffset? PickupEta { get; set; }

    public DateTimeOffset? DropoffEta { get; set; }

    /// <summary>Sandbox rule: the parcel comes back instead of being delivered.</summary>
    public bool WillReturn { get; init; }

    public string? UndeliverableReason { get; set; }

    public DeliveryResponse ToResponse() => new(
        "delivery",
        Id,
        QuoteId,
        Status,
        Complete,
        Courier,
        CourierImminent: Status is "pickup" or "dropoff",
        Created,
        Updated,
        "brl",
        Fee,
        $"https://simulator.local/track/{Id}",
        PickupEta,
        DropoffEta,
        ExternalId,
        ManifestReference,
        LiveMode: false,
        Uuid,
        UndeliverableReason);
}
