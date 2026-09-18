using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).ConfigureEmail();
        builder.Property(u => u.PasswordHash).HasMaxLength(User.PasswordHashMaxLength);

        // Fixed role set stored as text[] (PostgreSQL array) — no join table needed (D-27).
        builder.PrimitiveCollection(u => u.Roles)
            .ElementType(element => element.HasConversion<string>().HasMaxLength(32))
            .HasField("_roles")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(u => u.Email).IsUnique();

        builder.UseXminAsConcurrencyToken();
        builder.Ignore(u => u.DomainEvents);
    }
}
