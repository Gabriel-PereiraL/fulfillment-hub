using System.ComponentModel.DataAnnotations;
using FulfillmentHub.Domain.Orders;

namespace FulfillmentHub.Api.Orders;

/// <summary>
/// What a customer may send when placing an order: products, quantities and the delivery address.
/// Prices, totals, status and the customer id are never accepted from the client (anti-overposting).
/// </summary>
public sealed record PlaceOrderRequest(
    [property: Required, MinLength(1), MaxLength(Order.MaxItems)] IReadOnlyList<PlaceOrderItemRequest> Items,
    [property: Required] PlaceOrderAddressRequest DeliveryAddress);

public sealed record PlaceOrderItemRequest(
    [property: Required] Guid ProductId,
    [property: Range(1, OrderItem.MaxQuantity)] int Quantity);

public sealed record PlaceOrderAddressRequest(
    [property: Required, MaxLength(200)] string Street,
    [property: Required, MaxLength(20)] string Number,
    [property: MaxLength(100)] string? Complement,
    [property: Required, MaxLength(100)] string District,
    [property: Required, MaxLength(100)] string City,
    [property: Required, MaxLength(50)] string State,
    [property: Required, MaxLength(12)] string PostalCode,
    [property: MaxLength(2)] string? Country,
    [property: Range(-90, 90)] double? Latitude,
    [property: Range(-180, 180)] double? Longitude);

public sealed record CancelOrderRequest([property: MaxLength(200)] string? Note);
