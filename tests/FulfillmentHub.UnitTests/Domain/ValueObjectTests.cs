using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class ValueObjectTests
{
    [Fact]
    public void EmailAddress_IsNormalized()
    {
        EmailAddress.Of("  Ana.Souza@Example.COM ").Value.ShouldBe("ana.souza@example.com");
        EmailAddress.Of("a@b.co").ShouldBe(EmailAddress.Of("A@B.CO"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing@")]
    public void EmailAddress_RejectsInvalidValues(string value)
    {
        Should.Throw<DomainException>(() => EmailAddress.Of(value));
    }

    [Fact]
    public void PhoneNumber_IsNormalizedToE164_AndMasked()
    {
        var phone = PhoneNumber.Of("+55 (81) 99999-0000");

        phone.Value.ShouldBe("+5581999990000");
        phone.Masked.ShouldBe("+55*******0000");
    }

    [Theory]
    [InlineData("123")]
    [InlineData("+1234567890123456")]
    public void PhoneNumber_RejectsWrongLength(string value)
    {
        Should.Throw<DomainException>(() => PhoneNumber.Of(value));
    }

    [Fact]
    public void Address_NormalizesPostalCodeAndCountry()
    {
        var address = Address.Create(" Rua A ", "10", "", "Bairro", "Cidade", "PE", "50.000-000", "br");

        address.Street.ShouldBe("Rua A");
        address.Complement.ShouldBeNull();
        address.PostalCode.ShouldBe("50000000");
        address.Country.ShouldBe("BR");
        address.Latitude.ShouldBeNull();
    }

    [Fact]
    public void Address_RequiresBothCoordinatesOrNone()
    {
        Should.Throw<DomainException>(() =>
            Address.Create("Rua A", "10", null, "Bairro", "Cidade", "PE", "50000000", latitude: -8.0));
        Should.Throw<DomainException>(() =>
            Address.Create("Rua A", "10", null, "Bairro", "Cidade", "PE", "50000000", latitude: -8.0, longitude: 200));
    }

    [Fact]
    public void Address_RequiresMandatoryFields()
    {
        Should.Throw<DomainException>(() => Address.Create("", "10", null, "Bairro", "Cidade", "PE", "50000000"));
        Should.Throw<DomainException>(() => Address.Create("Rua", "10", null, "Bairro", "Cidade", "PE", "12"));
    }
}
