using FulfillmentHub.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Messaging;

internal sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");
        builder.HasKey(m => new { m.Consumer, m.MessageId });
        builder.Property(m => m.Consumer).HasMaxLength(ProcessedMessage.ConsumerMaxLength);
        builder.Property(m => m.MessageId).HasMaxLength(ProcessedMessage.MessageIdMaxLength);
        builder.HasIndex(m => m.ProcessedAt); // retention job (P2) sweeps by age
    }
}
