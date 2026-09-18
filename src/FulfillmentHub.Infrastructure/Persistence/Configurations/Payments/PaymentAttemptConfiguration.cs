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
        builder.Property(a => a.Outcome).AsString();
        builder.Property(a => a.ProviderErrorCode).HasMaxLength(PaymentAttempt.ErrorCodeMaxLength);
        builder.Property(a => a.ProviderReference).HasMaxLength(PaymentAttempt.ProviderReferenceMaxLength);
    }
}
