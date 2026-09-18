using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Deliveries;

internal sealed class DeliveryEventConfiguration : IEntityTypeConfiguration<DeliveryEvent>
{
    public void Configure(EntityTypeBuilder<DeliveryEvent> builder)
    {
        builder.ToTable("delivery_events");

        builder.HasKey(e => e.Id);
        // Ids are assigned by the aggregate (Guid v7). Without this, EF treats a new child found through a navigation
        // as an existing row (its key is "already set") and issues an UPDATE instead of an INSERT.
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ProviderEventId).HasMaxLength(DeliveryEvent.ProviderEventIdMaxLength);
        builder.Property(e => e.ProviderStatus).HasMaxLength(DeliveryEvent.ProviderStatusMaxLength);
        builder.Property(e => e.Disposition).AsString();

        // The aggregate already treats a repeated provider event id as a duplicate; the index makes the DB agree.
        builder.HasIndex("delivery_id", nameof(DeliveryEvent.ProviderEventId)).IsUnique();
    }
}
