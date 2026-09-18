using System.ComponentModel.DataAnnotations;
using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Application.Deliveries;

/// <summary>
/// The single place orders ship from (section <c>Fulfillment:Origin</c>). FulfillmentHub models one store/warehouse;
/// a multi-store catalogue is out of scope (D-51). Also carries the fallback fee charged when the provider cannot
/// quote at checkout.
/// </summary>
public sealed class FulfillmentOriginOptions
{
    public const string SectionName = "Fulfillment:Origin";

    [Required]
    [MaxLength(120)]
    public string Name { get; init; } = string.Empty;

    /// <summary>E.164 phone of the store, given to the courier.</summary>
    [Required]
    public string Phone { get; init; } = string.Empty;

    [Required]
    public string Street { get; init; } = string.Empty;

    [Required]
    public string Number { get; init; } = string.Empty;

    public string? Complement { get; init; }

    [Required]
    public string District { get; init; } = string.Empty;

    [Required]
    public string City { get; init; } = string.Empty;

    [Required]
    public string State { get; init; } = string.Empty;

    [Required]
    public string PostalCode { get; init; } = string.Empty;

    public string Country { get; init; } = "BR";

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    /// <summary>Charged when the provider cannot be quoted at checkout (outage); the difference is absorbed by the store.</summary>
    [Range(0, 1000)]
    public decimal EstimatedDeliveryFee { get; init; } = 15m;

    public DeliveryParty ToParty() => new(
        Name,
        Address.Create(Street, Number, Complement, District, City, State, PostalCode, Country, Latitude, Longitude),
        PhoneNumber.Of(Phone));
}
