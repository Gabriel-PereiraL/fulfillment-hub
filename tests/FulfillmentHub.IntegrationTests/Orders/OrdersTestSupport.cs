using System.Net.Http.Json;
using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.IntegrationTests.Orders;

internal static class OrdersTestSupport
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static async Task<Product> CreateProductAsync(ApiFixture api, int stock, decimal price = 10m)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var product = Product.Create("SKU-" + Guid.NewGuid().ToString("N")[..12], "Test product", Money.Of(price), stock, DateTimeOffset.UtcNow);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public static async Task<int> GetStockAsync(ApiFixture api, ProductId productId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync();
    }

    public static async Task<int> CountOrdersForProductAsync(ApiFixture api, ProductId productId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Orders.AsNoTracking().CountAsync(o => o.Items.Any(i => i.ProductId == productId));
    }

    public static object OrderBody(params (Guid ProductId, int Quantity)[] items) => new
    {
        items = items.Select(i => new { productId = i.ProductId, quantity = i.Quantity }).ToArray(),
        deliveryAddress = new
        {
            street = "Rua das Flores",
            number = "123",
            complement = "Apto 4",
            district = "Centro",
            city = "Recife",
            state = "PE",
            postalCode = "50000-000",
            country = "BR",
        },
    };

    public static Task<HttpResponseMessage> PlaceOrderAsync(HttpClient client, object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add(IdempotencyHeader, idempotencyKey);
        }

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
