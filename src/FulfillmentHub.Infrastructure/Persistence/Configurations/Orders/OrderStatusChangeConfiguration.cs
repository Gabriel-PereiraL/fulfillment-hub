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
        builder.Property(c => c.From).AsString();
        builder.Property(c => c.To).AsString();
        builder.Property(c => c.Reason).HasMaxLength(OrderStatusChange.ReasonMaxLength);
    }
}
