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
        builder.Property(i => i.Sku).HasMaxLength(Product.SkuMaxLength);
        builder.Property(i => i.ProductName).HasMaxLength(Product.NameMaxLength);
        builder.ComplexProperty(i => i.UnitPrice, money => money.ConfigureMoney());

        builder.HasIndex(i => i.ProductId);
        builder.Ignore(i => i.LineTotal);
    }
}
