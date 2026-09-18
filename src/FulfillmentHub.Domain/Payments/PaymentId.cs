using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Payments;

public readonly record struct PaymentId(Guid Value) : IStronglyTypedId<PaymentId>
{
    public static PaymentId New() => new(Guid.CreateVersion7());

    public static PaymentId From(Guid value) => new(value);

    public override string ToString() => Value.ToString();
}
