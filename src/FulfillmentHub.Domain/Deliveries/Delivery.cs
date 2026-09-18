using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Orders;

namespace FulfillmentHub.Domain.Deliveries;

/// <summary>
/// Delivery aggregate mirroring the provider's delivery. Provider events may arrive duplicated, delayed or out of
/// order (docs/INTEGRATIONS.md §1.5): every event is recorded, but the status only moves forward according to the
/// canonical order in <see cref="DeliveryStatus"/> and the provider's own timestamps.
/// </summary>
public sealed class Delivery : AggregateRoot<DeliveryId>
{
    public const int ProviderMaxLength = 40;
    public const int ProviderDeliveryIdMaxLength = 128;
    public const int TrackingUrlMaxLength = 512;

    private readonly List<DeliveryEvent> _events = [];

    private Delivery(DeliveryId id)
        : base(id)
    {
    }

    public OrderId OrderId { get; private set; }

    public DeliveryQuoteId QuoteId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string? ProviderDeliveryId { get; private set; }

    /// <summary>Key sent to the provider so retries of the creation call never produce two deliveries.</summary>
    public string ProviderIdempotencyKey { get; private set; } = null!;

    public DeliveryStatus Status { get; private set; }

    public string? TrackingUrl { get; private set; }

    public Money Fee { get; private set; } = null!;

    public CourierInfo? Courier { get; private set; }

    public DateTimeOffset? LastProviderEventAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<DeliveryEvent> Events => _events.AsReadOnly();

    public bool IsFinal => Status is DeliveryStatus.Delivered or DeliveryStatus.Cancelled or DeliveryStatus.Returned;

    public bool IsActive => !IsFinal;

    /// <summary>The courier already has the parcel; the provider will refuse cancellation from here on.</summary>
    public bool HasBeenPickedUp => Status is DeliveryStatus.PickupComplete or DeliveryStatus.Dropoff or DeliveryStatus.Delivered;

    public static Delivery Request(DeliveryQuote quote, int attempt, DateTimeOffset now)
    {
        if (quote.IsExpired(now))
        {
            throw new DomainException($"Quote '{quote.ProviderQuoteId}' expired at {quote.ExpiresAt:O} and cannot be used.");
        }

        if (attempt <= 0)
        {
            throw new DomainException("Delivery request attempt must be positive.");
        }

        return new Delivery(DeliveryId.New())
        {
            OrderId = quote.OrderId,
            QuoteId = quote.Id,
            Provider = quote.Provider,
            ProviderIdempotencyKey = $"order-{quote.OrderId.Value:N}-delivery-{attempt}",
            Status = DeliveryStatus.Requested,
            Fee = quote.Fee,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>The provider accepted the creation call.</summary>
    public void ConfirmCreated(string providerDeliveryId, string? trackingUrl, Money fee, DateTimeOffset now)
    {
        if (Status != DeliveryStatus.Requested)
        {
            throw new InvalidStateTransitionException(nameof(Delivery), Status.ToString(), DeliveryStatus.Pending.ToString());
        }

        if (string.IsNullOrWhiteSpace(providerDeliveryId))
        {
            throw new DomainException("Provider delivery id is required.");
        }

        ProviderDeliveryId = providerDeliveryId;
        TrackingUrl = trackingUrl;
        Fee = fee;
        Status = DeliveryStatus.Pending;
        UpdatedAt = now;
    }

    /// <summary>
    /// Applies a provider status event. Duplicates (same provider event id), stale events (older than the last
    /// applied one) and backwards transitions are recorded but do not change the status.
    /// </summary>
    public DeliveryEventDisposition ApplyProviderEvent(
        string providerEventId,
        string providerStatus,
        DeliveryStatus reported,
        DateTimeOffset occurredAt,
        CourierInfo? courier,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(providerEventId))
        {
            throw new DomainException("Provider event id is required.");
        }

        if (Status == DeliveryStatus.Requested)
        {
            throw new InvalidStateTransitionException(nameof(Delivery), Status.ToString(), reported.ToString());
        }

        var disposition = Classify(providerEventId, reported, occurredAt);
        _events.Add(DeliveryEvent.Record(providerEventId, providerStatus, occurredAt, now, disposition));

        if (disposition == DeliveryEventDisposition.Applied)
        {
            Status = reported;
            LastProviderEventAt = occurredAt;

            if (courier is not null)
            {
                Courier = courier;
            }
        }

        UpdatedAt = now;
        return disposition;
    }

    /// <summary>Local cancellation request (customer/operator) confirmed by the provider.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status == DeliveryStatus.Cancelled)
        {
            return;
        }

        if (HasBeenPickedUp || Status == DeliveryStatus.Returned)
        {
            throw new InvalidStateTransitionException(nameof(Delivery), Status.ToString(), DeliveryStatus.Cancelled.ToString());
        }

        Status = DeliveryStatus.Cancelled;
        UpdatedAt = now;
    }

    private DeliveryEventDisposition Classify(string providerEventId, DeliveryStatus reported, DateTimeOffset occurredAt)
    {
        if (_events.Any(e => e.ProviderEventId == providerEventId))
        {
            return DeliveryEventDisposition.Duplicate;
        }

        if (reported == DeliveryStatus.Requested)
        {
            return DeliveryEventDisposition.Conflict;
        }

        if (LastProviderEventAt is { } last && occurredAt < last)
        {
            return DeliveryEventDisposition.Stale;
        }

        if (reported == Status)
        {
            return DeliveryEventDisposition.Duplicate;
        }

        if (IsFinal)
        {
            // A final status never regresses; a different final status coming later is an operational conflict.
            return reported is DeliveryStatus.Cancelled or DeliveryStatus.Returned or DeliveryStatus.Delivered
                ? DeliveryEventDisposition.Conflict
                : DeliveryEventDisposition.OutOfOrder;
        }

        var movesForward = reported is DeliveryStatus.Cancelled or DeliveryStatus.Returned || reported > Status;

        return movesForward ? DeliveryEventDisposition.Applied : DeliveryEventDisposition.OutOfOrder;
    }
}
