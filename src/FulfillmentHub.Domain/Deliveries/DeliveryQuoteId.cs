using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Deliveries;

public readonly record struct DeliveryQuoteId(Guid Value) : IStronglyTypedId<DeliveryQuoteId>
{
    public static DeliveryQuoteId New() => new(Guid.CreateVersion7());

    public static DeliveryQuoteId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
