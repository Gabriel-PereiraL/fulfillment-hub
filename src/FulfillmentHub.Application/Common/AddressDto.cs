using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Application.Common;

public sealed record AddressDto(
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string State,
    string PostalCode,
    string Country,
    double? Latitude,
    double? Longitude)
{
    public static AddressDto From(Address address) => new(
        address.Street,
        address.Number,
        address.Complement,
        address.District,
        address.City,
        address.State,
        address.PostalCode,
        address.Country,
        address.Latitude,
        address.Longitude);
}
