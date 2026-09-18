using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Orders;

/// <summary>A line of an order with a price snapshot taken when the order was placed.</summary>
public sealed class OrderItem
{
    public const int MaxQuantity = 99;

    private OrderItem()
    {
    }

    public Guid Id { get; private set; }

    public ProductId ProductId { get; private set; }

    public string Sku { get; private set; } = null!;

    public string ProductName { get; private set; } = null!;

    public Money UnitPrice { get; private set; } = null!;

    public int Quantity { get; private set; }

    public Money LineTotal => UnitPrice * Quantity;

    internal static OrderItem Create(Product product, int quantity)
    {
        if (quantity is <= 0 or > MaxQuantity)
        {
            throw new DomainException($"Item quantity must be between 1 and {MaxQuantity}.");
        }

        if (!product.IsActive)
        {
            throw new DomainException($"Product '{product.Sku}' is inactive.");
        }

        return new OrderItem
        {
            Id = Guid.CreateVersion7(),
            ProductId = product.Id,
            Sku = product.Sku,
            ProductName = product.Name,
            UnitPrice = product.UnitPrice,
            Quantity = quantity,
        };
    }
}
