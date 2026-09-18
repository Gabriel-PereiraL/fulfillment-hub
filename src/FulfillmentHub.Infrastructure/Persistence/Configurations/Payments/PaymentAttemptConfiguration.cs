using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Payments;

internal sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts");

        builder.HasKey(a => a.Id);
        // Ids are assigned by the aggregate (Guid v7). Without this, EF treats a new child found through a navigation
        // as an existing row (its key is "already set") and issues an UPDATE instead of an INSERT.
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Outcome).AsString();
        builder.Property(a => a.ProviderErrorCode).HasMaxLength(PaymentAttempt.ErrorCodeMaxLength);
        builder.Property(a => a.ProviderReference).HasMaxLength(PaymentAttempt.ProviderReferenceMaxLength);
    }
}
