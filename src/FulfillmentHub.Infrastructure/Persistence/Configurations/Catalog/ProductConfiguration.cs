using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Catalog;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
            table.HasCheckConstraint("ck_products_stock_quantity_non_negative", "stock_quantity >= 0"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Sku).HasMaxLength(Product.SkuMaxLength);
        builder.Property(p => p.Name).HasMaxLength(Product.NameMaxLength);
        builder.ComplexProperty(p => p.UnitPrice, money => money.ConfigureMoney());

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.IsActive);

        // Optimistic concurrency for stock reservations (docs/DOMAIN.md §3, D-15).
        builder.UseXminAsConcurrencyToken();

        builder.Ignore(p => p.DomainEvents);
    }
}
