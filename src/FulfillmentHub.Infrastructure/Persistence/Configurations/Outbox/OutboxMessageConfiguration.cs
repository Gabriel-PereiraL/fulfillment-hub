using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Outbox;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(OutboxMessage.TypeMaxLength);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.Status).AsString(16);
        builder.Property(m => m.LastError).HasMaxLength(OutboxMessage.LastErrorMaxLength);
        builder.Property(m => m.CorrelationId).HasMaxLength(64);
        builder.Property(m => m.TraceParent).HasMaxLength(64);
        // The publisher's poll: pending messages that are due, oldest first.
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
        builder.HasIndex(m => m.AggregateId);
        builder.UseXminAsConcurrencyToken();
    }
}
