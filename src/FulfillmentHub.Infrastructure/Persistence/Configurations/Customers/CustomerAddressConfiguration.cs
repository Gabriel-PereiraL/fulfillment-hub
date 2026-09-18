using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Infrastructure.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Configurations.Customers;

internal sealed class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.ToTable("customer_addresses");

        builder.HasKey(a => a.Id);
        // Ids are assigned by the aggregate (Guid v7). Without this, EF treats a new child found through a navigation
        // as an existing row (its key is "already set") and issues an UPDATE instead of an INSERT.
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Label).HasMaxLength(CustomerAddress.LabelMaxLength);
        builder.ComplexProperty(a => a.Address, address => address.ConfigureAddress());
    }
}
