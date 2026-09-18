using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Deliveries;

internal sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Status).AsString();
        builder.Property(d => d.Provider).HasMaxLength(Delivery.ProviderMaxLength);
        builder.Property(d => d.ProviderDeliveryId).HasMaxLength(Delivery.ProviderDeliveryIdMaxLength);
        builder.Property(d => d.ProviderIdempotencyKey).HasMaxLength(128);
        builder.Property(d => d.TrackingUrl).HasMaxLength(Delivery.TrackingUrlMaxLength);
        builder.ComplexProperty(d => d.Fee, money => money.ConfigureMoney());
        builder.ComplexProperty(d => d.Courier, courier =>
        {
            courier.Property(c => c.Name).HasMaxLength(120);
            courier.Property(c => c.PhoneMasked).HasMaxLength(16);
            courier.Property(c => c.VehicleType).HasMaxLength(40);
            courier.Property(c => c.Latitude);
            courier.Property(c => c.Longitude);
        });

        builder.HasMany(d => d.Events)
            .WithOne()
            .HasForeignKey("delivery_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(d => d.Events).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // One active delivery per order (docs/DOMAIN.md §7).
        builder.HasIndex(d => d.OrderId)
            .IsUnique()
            .HasFilter("status NOT IN ('Cancelled', 'Returned')")
            .HasDatabaseName("ux_deliveries_active_per_order");
        builder.HasIndex(d => new { d.Provider, d.ProviderDeliveryId }).IsUnique();
        builder.HasIndex(d => d.Status);

        builder.UseXminAsConcurrencyToken();
        builder.Ignore(d => d.DomainEvents);
        builder.Ignore(d => d.IsFinal);
        builder.Ignore(d => d.IsActive);
        builder.Ignore(d => d.HasBeenPickedUp);
    }
}
