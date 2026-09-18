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
        builder.Property(a => a.Label).HasMaxLength(CustomerAddress.LabelMaxLength);
        builder.ComplexProperty(a => a.Address, address => address.ConfigureAddress());
    }
}
