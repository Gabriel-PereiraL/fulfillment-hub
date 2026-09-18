namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

// Wire contracts: the documented subset of the Uber Direct API reproduced by the simulator (docs/INTEGRATIONS.md §2.1).
// Serialized as snake_case JSON. Addresses travel as a JSON string of a structured address, like the real API.

internal sealed record ProviderTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string? Scope);

internal sealed record ProviderStructuredAddress(string[] StreetAddress, string City, string State, string ZipCode, string Country);

internal sealed record ProviderQuoteRequest(
    string PickupAddress,
    string DropoffAddress,
    double? PickupLatitude,
    double? PickupLongitude,
    double? DropoffLatitude,
    double? DropoffLongitude,
    string PickupPhoneNumber,
    string DropoffPhoneNumber,
    long? ManifestTotalValue);

internal sealed record ProviderQuoteResponse(
    string Id,
    DateTimeOffset Created,
    DateTimeOffset Expires,
    long Fee,
    string Currency,
    DateTimeOffset DropoffEta,
    int Duration,
    int PickupDuration);

internal sealed record ProviderManifestItem(string Name, int Quantity, string Size, long Price);

internal sealed record ProviderCreateDeliveryRequest(
    string PickupName,
    string PickupAddress,
    string PickupPhoneNumber,
    string DropoffName,
    string DropoffAddress,
    string DropoffPhoneNumber,
    ProviderManifestItem[] ManifestItems,
    string? QuoteId,
    string ManifestReference,
    long ManifestTotalValue,
    string IdempotencyKey,
    string ExternalId);

internal sealed record ProviderLatLng(double Lat, double Lng);

internal sealed record ProviderCourier(string Name, string? VehicleType, string? PhoneNumber, ProviderLatLng? Location);

internal sealed record ProviderDeliveryResponse(
    string Id,
    string? QuoteId,
    string Status,
    bool Complete,
    ProviderCourier? Courier,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    string Currency,
    long Fee,
    string? TrackingUrl,
    string? ExternalId);

internal sealed record ProviderErrorResponse(string? Code, string? Message, Dictionary<string, string>? Metadata);
