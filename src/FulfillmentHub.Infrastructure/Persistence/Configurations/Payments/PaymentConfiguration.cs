using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Payments;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Status).AsString();
        builder.Property(p => p.Provider).HasMaxLength(Payment.ProviderMaxLength);
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(Payment.ProviderPaymentIdMaxLength);
        builder.Property(p => p.ProviderIdempotencyKey).HasMaxLength(128);
        builder.Property(p => p.FailureReason).HasMaxLength(Payment.FailureReasonMaxLength);
        builder.ComplexProperty(p => p.Amount, money => money.ConfigureMoney());

        builder.HasMany(p => p.Attempts)
            .WithOne()
            .HasForeignKey("payment_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // One non-failed/non-cancelled payment per order (docs/DOMAIN.md §6).
        builder.HasIndex(p => p.OrderId)
            .IsUnique()
            .HasFilter("status NOT IN ('Failed', 'Cancelled')")
            .HasDatabaseName("ux_payments_active_per_order");
        builder.HasIndex(p => new { p.Provider, p.ProviderPaymentId }).IsUnique();
        builder.HasIndex(p => p.Status);

        builder.UseXminAsConcurrencyToken();
        builder.Ignore(p => p.DomainEvents);
        builder.Ignore(p => p.IsFinal);
    }
}
