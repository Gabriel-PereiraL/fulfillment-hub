using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Conventions;

internal static class ConcurrencyConfigurationExtensions
{
    /// <summary>
    /// Optimistic concurrency backed by PostgreSQL's <c>xmin</c> system column: Npgsql maps a <c>uint</c> row version
    /// to it, so no extra column is needed (D-15). Concurrent updates surface as <see cref="DbUpdateConcurrencyException"/>.
    /// </summary>
    public static EntityTypeBuilder<TEntity> UseXminAsConcurrencyToken<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>("xmin").IsRowVersion();
        return builder;
    }
}
