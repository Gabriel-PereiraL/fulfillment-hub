using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderStatusChangeConfiguration : IEntityTypeConfiguration<OrderStatusChange>
{
    public void Configure(EntityTypeBuilder<OrderStatusChange> builder)
    {
        builder.ToTable("order_status_changes");

        builder.HasKey(c => c.Id);
        // Ids are assigned by the aggregate (Guid v7). Without this, EF treats a new child found through a navigation
        // as an existing row (its key is "already set") and issues an UPDATE instead of an INSERT.
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.From).AsString();
        builder.Property(c => c.To).AsString();
        builder.Property(c => c.Reason).HasMaxLength(OrderStatusChange.ReasonMaxLength);
    }
}
