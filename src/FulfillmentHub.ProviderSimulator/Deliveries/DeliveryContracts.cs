using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.ProviderSimulator.Deliveries;

// Wire contracts of the simulated delivery provider: a documented subset of the Uber Direct API field names
// (docs/INTEGRATIONS.md §1–§2; reference spec in the private .ai/reference folder). snake_case on the wire.
// This is a simulator for educational and portfolio purposes; it does not talk to Uber.

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Scope);

/// <summary>Addresses travel as a JSON string of this structure, as in the real API.</summary>
public sealed record StructuredAddress(string[] StreetAddress, string City, string State, string ZipCode, string Country);

public sealed record DeliveryQuoteRequest(
    [property: Required] string PickupAddress,
    [property: Required] string DropoffAddress,
    double? PickupLatitude,
    double? PickupLongitude,
    double? DropoffLatitude,
    double? DropoffLongitude,
    DateTimeOffset? PickupReadyDt,
    DateTimeOffset? DropoffReadyDt,
    string? PickupPhoneNumber,
    string? DropoffPhoneNumber,
    long? ManifestTotalValue,
    string? ExternalStoreId);

public sealed record DeliveryQuoteResponse(
    string Kind,
    string Id,
    DateTimeOffset Created,
    DateTimeOffset Expires,
    long Fee,
    string Currency,
    string CurrencyType,
    DateTimeOffset DropoffEta,
    int Duration,
    int PickupDuration,
    DateTimeOffset DropoffDeadline);

public sealed record ManifestItem(
    [property: Required] string Name,
    [property: Range(1, 1000)] int Quantity,
    string? Size,
    long? Price);

public sealed record CreateDeliveryRequest(
    [property: Required] string PickupName,
    [property: Required] string PickupAddress,
    [property: Required] string PickupPhoneNumber,
    [property: Required] string DropoffName,
    [property: Required] string DropoffAddress,
    [property: Required] string DropoffPhoneNumber,
    [property: Required, MinLength(1)] ManifestItem[] ManifestItems,
    string? QuoteId,
    string? ManifestReference,
    long? ManifestTotalValue,
    string? IdempotencyKey,
    string? ExternalId,
    string? PickupNotes,
    string? DropoffNotes);

public sealed record LatLng(double Lat, double Lng);

public sealed record CourierResponse(string Name, double Rating, string VehicleType, string PhoneNumber, LatLng Location, string? ImgHref);

public sealed record DeliveryResponse(
    string Kind,
    string Id,
    string? QuoteId,
    string Status,
    bool Complete,
    CourierResponse? Courier,
    bool CourierImminent,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    string Currency,
    long Fee,
    string TrackingUrl,
    DateTimeOffset? PickupEta,
    DateTimeOffset? DropoffEta,
    string? ExternalId,
    string? ManifestReference,
    bool LiveMode,
    string Uuid,
    string? UndeliverableReason);

/// <summary><c>event.delivery_status</c> as documented for the real provider, with the delivery object in <c>data</c>.</summary>
public sealed record DeliveryStatusEvent(
    string Id,
    string Kind,
    DateTimeOffset Created,
    string Status,
    string DeliveryId,
    string CustomerId,
    bool LiveMode,
    DeliveryResponse Data);
