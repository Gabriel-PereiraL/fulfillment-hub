using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Orders;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Number)
            .HasDefaultValueSql($"nextval('{FulfillmentHubDbContext.OrderNumberSequence}')")
            .ValueGeneratedOnAdd();

        builder.Property(o => o.Status).AsString();
        builder.Property(o => o.CancellationReason).AsString();
        builder.Property(o => o.IdempotencyKey).HasMaxLength(Order.IdempotencyKeyMaxLength);

        builder.ComplexProperty(o => o.DeliveryAddress, address => address.ConfigureAddress());
        builder.ComplexProperty(o => o.Subtotal, money => money.ConfigureMoney());
        builder.ComplexProperty(o => o.DeliveryFee, money => money.ConfigureMoney());
        builder.ComplexProperty(o => o.Total, money => money.ConfigureMoney());

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey("order_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(o => o.StatusHistory)
            .WithOne()
            .HasForeignKey("order_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        builder.HasIndex(o => o.Status);

        builder.UseXminAsConcurrencyToken();
        builder.Ignore(o => o.DomainEvents);
        builder.Ignore(o => o.IsFinal);
    }
}
