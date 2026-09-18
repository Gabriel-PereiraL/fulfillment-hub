namespace FulfillmentHub.Domain.Common;

/// <summary>
/// Postal address, used both as a customer's saved address and as the immutable delivery snapshot of an order.
/// Persisted as an EF Core complex type (value semantics).
/// </summary>
public sealed record Address
{
    public string Street { get; }

    public string Number { get; }

    public string? Complement { get; }

    public string District { get; }

    public string City { get; }

    public string State { get; }

    public string PostalCode { get; }

    public string Country { get; }

    public double? Latitude { get; }

    public double? Longitude { get; }

    private Address(
        string street,
        string number,
        string? complement,
        string district,
        string city,
        string state,
        string postalCode,
        string country,
        double? latitude,
        double? longitude)
    {
        Street = street;
        Number = number;
        Complement = complement;
        District = district;
        City = city;
        State = state;
        PostalCode = postalCode;
        Country = country;
        Latitude = latitude;
        Longitude = longitude;
    }

    public static Address Create(
        string street,
        string number,
        string? complement,
        string district,
        string city,
        string state,
        string postalCode,
        string country = "BR",
        double? latitude = null,
        double? longitude = null)
    {
        var normalizedPostalCode = new string((postalCode ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray());

        if (normalizedPostalCode.Length is < 4 or > 10)
        {
            throw new DomainException($"'{postalCode}' is not a valid postal code.");
        }

        if (latitude is < -90 or > 90 || longitude is < -180 or > 180 || (latitude is null) != (longitude is null))
        {
            throw new DomainException("Latitude and longitude must be provided together and be within range.");
        }

        return new Address(
            Required(street, nameof(street), 200),
            Required(number, nameof(number), 20),
            string.IsNullOrWhiteSpace(complement) ? null : complement.Trim(),
            Required(district, nameof(district), 100),
            Required(city, nameof(city), 100),
            Required(state, nameof(state), 50),
            normalizedPostalCode,
            Required(country, nameof(country), 2).ToUpperInvariant(),
            latitude,
            longitude);
    }

    private static string Required(string? value, string field, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        return trimmed.Length is 0 || trimmed.Length > maxLength
            ? throw new DomainException($"Address {field} is required and must have at most {maxLength} characters.")
            : trimmed;
    }
}
