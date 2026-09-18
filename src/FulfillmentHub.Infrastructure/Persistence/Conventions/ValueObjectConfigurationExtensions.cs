using FulfillmentHub.Domain.Common;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FulfillmentHub.Infrastructure.Persistence.Conventions;

/// <summary>Shared column mapping for value objects so every table stores them the same way.</summary>
internal static class ValueObjectConfigurationExtensions
{
    public static void ConfigureMoney(this ComplexPropertyBuilder<Money> money)
    {
        money.Property(m => m.Amount).HasPrecision(18, 2);
        money.Property(m => m.Currency).HasMaxLength(3);
    }

    public static void ConfigureAddress(this ComplexPropertyBuilder<Address> address)
    {
        address.Property(a => a.Street).HasMaxLength(200);
        address.Property(a => a.Number).HasMaxLength(20);
        address.Property(a => a.Complement).HasMaxLength(100);
        address.Property(a => a.District).HasMaxLength(100);
        address.Property(a => a.City).HasMaxLength(100);
        address.Property(a => a.State).HasMaxLength(50);
        address.Property(a => a.PostalCode).HasMaxLength(10);
        address.Property(a => a.Country).HasMaxLength(2);
        // Nullable numeric members of a complex type are not discovered by convention; map them explicitly.
        address.Property(a => a.Latitude);
        address.Property(a => a.Longitude);
    }

    public static PropertyBuilder<EmailAddress> ConfigureEmail(this PropertyBuilder<EmailAddress> email) =>
        email.HasConversion(e => e.Value, value => EmailAddress.Of(value)).HasMaxLength(EmailAddress.MaxLength);

    public static PropertyBuilder<PhoneNumber> ConfigurePhone(this PropertyBuilder<PhoneNumber> phone) =>
        phone.HasConversion(p => p.Value, value => PhoneNumber.Of(value)).HasMaxLength(PhoneNumber.MaxLength);

    public static PropertyBuilder<TEnum> AsString<TEnum>(this PropertyBuilder<TEnum> property, int maxLength = 32)
        where TEnum : struct, Enum =>
        property.HasConversion<string>().HasMaxLength(maxLength);

    public static PropertyBuilder<TEnum?> AsString<TEnum>(this PropertyBuilder<TEnum?> property, int maxLength = 32)
        where TEnum : struct, Enum =>
        property.HasConversion<string?>().HasMaxLength(maxLength);
}
