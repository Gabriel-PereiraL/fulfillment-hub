using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Deliveries;

public readonly record struct DeliveryId(Guid Value) : IStronglyTypedId<DeliveryId>
{
    public static DeliveryId New() => new(Guid.CreateVersion7());

    public static DeliveryId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
