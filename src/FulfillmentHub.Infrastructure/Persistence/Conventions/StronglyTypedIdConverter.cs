using FulfillmentHub.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FulfillmentHub.Infrastructure.Persistence.Conventions;

/// <summary>Maps any <see cref="IStronglyTypedId{TSelf}"/> to a plain <c>uuid</c> column.</summary>
public sealed class StronglyTypedIdConverter<TId>() : ValueConverter<TId, Guid>(id => id.Value, value => FromGuid(value))
    where TId : struct, IStronglyTypedId<TId>
{
    // Static abstract interface members cannot appear inside expression trees; this indirection keeps the tree simple.
    private static TId FromGuid(Guid value) => TId.From(value);
}
