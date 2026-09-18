using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Orders;

public readonly record struct OrderId(Guid Value) : IStronglyTypedId<OrderId>
{
    public static OrderId New() => new(Guid.CreateVersion7());

    public static OrderId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
