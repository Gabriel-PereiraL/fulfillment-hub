using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Catalog;

public sealed class Product : AggregateRoot<ProductId>
{
    public const int SkuMaxLength = 32;
    public const int NameMaxLength = 120;

    private Product(ProductId id)
        : base(id)
    {
    }

    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public Money UnitPrice { get; private set; } = null!;

    public int StockQuantity { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static Product Create(string sku, string name, Money unitPrice, int initialStock, DateTimeOffset now)
    {
        var normalizedSku = sku?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalizedSku.Length is 0 or > SkuMaxLength)
        {
            throw new DomainException($"SKU is required and must have at most {SkuMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Product name is required and must have at most {NameMaxLength} characters.");
        }

        if (unitPrice.Amount <= 0m)
        {
            throw new DomainException("Product unit price must be greater than zero.");
        }

        if (initialStock < 0)
        {
            throw new DomainException("Initial stock cannot be negative.");
        }

        return new Product(ProductId.New())
        {
            Sku = normalizedSku,
            Name = name.Trim(),
            UnitPrice = unitPrice,
            StockQuantity = initialStock,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Reserves stock for an order. Throws when the requested quantity is not available.</summary>
    public void Reserve(int quantity, DateTimeOffset now)
    {
        EnsurePositive(quantity);

        if (!IsActive)
        {
            throw new DomainException($"Product '{Sku}' is inactive and cannot be reserved.");
        }

        if (quantity > StockQuantity)
        {
            throw new InsufficientStockException(Sku, quantity, StockQuantity);
        }

        StockQuantity -= quantity;
        UpdatedAt = now;
    }

    /// <summary>Returns previously reserved stock (order cancelled or payment failed).</summary>
    public void Release(int quantity, DateTimeOffset now)
    {
        EnsurePositive(quantity);
        StockQuantity += quantity;
        UpdatedAt = now;
    }

    public void Restock(int quantity, DateTimeOffset now) => Release(quantity, now);

    public void ChangePrice(Money newPrice, DateTimeOffset now)
    {
        if (newPrice.Amount <= 0m)
        {
            throw new DomainException("Product unit price must be greater than zero.");
        }

        UnitPrice = newPrice;
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    private static void EnsurePositive(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }
    }
}
