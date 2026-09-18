using FulfillmentHub.Infrastructure.Persistence.Conventions;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Webhooks;

internal sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Provider).HasMaxLength(WebhookEvent.ProviderMaxLength);
        builder.Property(e => e.ProviderEventId).HasMaxLength(WebhookEvent.ProviderEventIdMaxLength);
        builder.Property(e => e.EventType).HasMaxLength(WebhookEvent.EventTypeMaxLength);
        builder.Property(e => e.Payload).HasColumnType("jsonb");
        builder.Property(e => e.Status).AsString(16);
        builder.Property(e => e.LastError).HasMaxLength(WebhookEvent.LastErrorMaxLength);
        builder.Property(e => e.CorrelationId).HasMaxLength(64);

        // Deduplication guarantee (ADR-010): the same provider event can only be recorded once.
        builder.HasIndex(e => new { e.Provider, e.ProviderEventId }).IsUnique();
        builder.HasIndex(e => new { e.Status, e.ReceivedAt });

        builder.UseXminAsConcurrencyToken();
    }
}
