using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Deliveries;

internal sealed class DeliveryQuoteConfiguration : IEntityTypeConfiguration<DeliveryQuote>
{
    public void Configure(EntityTypeBuilder<DeliveryQuote> builder)
    {
        builder.ToTable("delivery_quotes");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Provider).HasMaxLength(DeliveryQuote.ProviderMaxLength);
        builder.Property(q => q.ProviderQuoteId).HasMaxLength(DeliveryQuote.ProviderQuoteIdMaxLength);
        builder.ComplexProperty(q => q.Fee, money => money.ConfigureMoney());

        builder.HasIndex(q => q.OrderId);
        builder.HasIndex(q => new { q.Provider, q.ProviderQuoteId }).IsUnique();

        builder.Ignore(q => q.DomainEvents);
    }
}
