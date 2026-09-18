namespace FulfillmentHub.Domain.Common;

/// <summary>
/// Contract implemented by every strongly-typed identifier so persistence can convert them generically.
/// </summary>
public interface IStronglyTypedId<TSelf>
    where TSelf : struct, IStronglyTypedId<TSelf>
{
    Guid Value { get; }

    static abstract TSelf From(Guid value);
}
