using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Application.Catalog;

public sealed record ProductDto(Guid Id, string Sku, string Name, MoneyDto UnitPrice, int StockQuantity, bool IsActive);

/// <summary>Catalogue reads. Customers see active products; stock is exposed so clients can avoid doomed orders.</summary>
public sealed class ProductQueries(IFulfillmentHubDbContext db)
{
    public async Task<IReadOnlyList<ProductDto>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var products = await db.Products
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        return products
            .Select(p => new ProductDto(p.Id.Value, p.Sku, p.Name, MoneyDto.From(p.UnitPrice), p.StockQuantity, p.IsActive))
            .ToList();
    }
}
