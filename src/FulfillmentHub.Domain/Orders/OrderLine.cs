using FulfillmentHub.Domain.Catalog;

namespace FulfillmentHub.Domain.Orders;

/// <summary>Input line for <see cref="Order.Place"/>: the product (for the price snapshot) and the requested quantity.</summary>
public sealed record OrderLine(Product Product, int Quantity);
