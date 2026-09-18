using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Application.Orders;

/// <summary>Returns an order's reserved units to stock, in the caller's unit of work (used by every cancellation path).</summary>
internal static class StockRelease
{
    public static async Task ReleaseAsync(IFulfillmentHubDbContext db, Order order, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

        foreach (var item in order.Items)
        {
            products.Single(p => p.Id == item.ProductId).Release(item.Quantity, now);
        }
    }
}
