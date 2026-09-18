using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
            table.HasCheckConstraint("ck_order_items_quantity_range", $"quantity BETWEEN 1 AND {OrderItem.MaxQuantity}"));

        builder.HasKey(i => i.Id);
        // Ids are assigned by the aggregate (Guid v7). Without this, EF treats a new child found through a navigation
        // as an existing row (its key is "already set") and issues an UPDATE instead of an INSERT.
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Sku).HasMaxLength(Product.SkuMaxLength);
        builder.Property(i => i.ProductName).HasMaxLength(Product.NameMaxLength);
        builder.ComplexProperty(i => i.UnitPrice, money => money.ConfigureMoney());

        builder.HasIndex(i => i.ProductId);
        builder.Ignore(i => i.LineTotal);
    }
}
