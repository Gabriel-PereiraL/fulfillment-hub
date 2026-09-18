using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Orders;

namespace FulfillmentHub.Domain.Deliveries;

/// <summary>A delivery offer from the provider. Immutable; usable only before <see cref="ExpiresAt"/>.</summary>
public sealed class DeliveryQuote : AggregateRoot<DeliveryQuoteId>
{
    public const int ProviderMaxLength = 40;
    public const int ProviderQuoteIdMaxLength = 128;

    private DeliveryQuote(DeliveryQuoteId id)
        : base(id)
    {
    }

    public OrderId OrderId { get; private set; }

    public string Provider { get; private set; } = null!;

    public string ProviderQuoteId { get; private set; } = null!;

    public Money Fee { get; private set; } = null!;

    public DateTimeOffset EstimatedDropoffAt { get; private set; }

    public int DurationMinutes { get; private set; }

    public int PickupDurationMinutes { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DeliveryQuote Create(
        OrderId orderId,
        string provider,
        string providerQuoteId,
        Money fee,
        DateTimeOffset estimatedDropoffAt,
        int durationMinutes,
        int pickupDurationMinutes,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerQuoteId))
        {
            throw new DomainException("Quote provider and provider quote id are required.");
        }

        if (fee.IsNegative)
        {
            throw new DomainException("Delivery fee cannot be negative.");
        }

        if (expiresAt <= now)
        {
            throw new DomainException("A quote cannot be created already expired.");
        }

        return new DeliveryQuote(DeliveryQuoteId.New())
        {
            OrderId = orderId,
            Provider = provider,
            ProviderQuoteId = providerQuoteId,
            Fee = fee,
            EstimatedDropoffAt = estimatedDropoffAt,
            DurationMinutes = durationMinutes,
            PickupDurationMinutes = pickupDurationMinutes,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
