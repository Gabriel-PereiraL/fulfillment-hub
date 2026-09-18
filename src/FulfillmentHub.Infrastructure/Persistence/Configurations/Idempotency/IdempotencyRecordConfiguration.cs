using FulfillmentHub.Infrastructure.Idempotency;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Idempotency;

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        // The primary key is what makes concurrent requests with the same key collide (exactly one wins the insert).
        builder.HasKey(r => new { r.Scope, r.Key });
        builder.Property(r => r.Scope).HasMaxLength(IdempotencyRecord.ScopeMaxLength);
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.KeyMaxLength);
        builder.Property(r => r.RequestHash).HasMaxLength(IdempotencyRecord.HashLength);
        builder.Property(r => r.Status).AsString(16);
        builder.Property(r => r.ResponseContentType).HasMaxLength(64);
        builder.Property(r => r.ResponseLocation).HasMaxLength(512);

        builder.HasIndex(r => r.ExpiresAt);

        builder.UseXminAsConcurrencyToken();
    }
}
