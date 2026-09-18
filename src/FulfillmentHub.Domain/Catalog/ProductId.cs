using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Catalog;

public readonly record struct ProductId(Guid Value) : IStronglyTypedId<ProductId>
{
    public static ProductId New() => new(Guid.CreateVersion7());

    public static ProductId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
