using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Customers;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength);
        builder.Property(c => c.Email).ConfigureEmail();
        builder.Property(c => c.Phone).ConfigurePhone();

        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasIndex(c => c.Email).IsUnique();

        builder.HasMany(c => c.Addresses)
            .WithOne()
            .HasForeignKey("customer_id")
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.UseXminAsConcurrencyToken();
        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.DefaultAddress);
    }
}
