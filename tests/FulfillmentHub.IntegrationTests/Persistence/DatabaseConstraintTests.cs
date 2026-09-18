using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FulfillmentHub.IntegrationTests.Persistence;

/// <summary>
/// The database is the last line of defence (docs/DOMAIN.md §12): these tests bypass the domain rules on
/// purpose and prove that PostgreSQL rejects the invalid state anyway.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class DatabaseConstraintTests(ApiFixture api)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StockQuantity_CannotGoNegative_EvenViaRawSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = NewProduct(stock: 1);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        var act = () => db.Database.ExecuteSqlAsync(
            $"UPDATE products SET stock_quantity = stock_quantity - 2 WHERE id = {product.Id.Value}", ct);

        var exception = await Should.ThrowAsync<PostgresException>(act);
        exception.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        exception.ConstraintName.ShouldBe("ck_products_stock_quantity_non_negative");
    }

    [Fact]
    public async Task Sku_MustBeUnique()
    {
        var ct = TestContext.Current.CancellationToken;
        var sku = "SKU-" + Guid.NewGuid().ToString("N")[..12];
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        db.Products.Add(Product.Create(sku, "One", Money.Of(1m), 1, Now));
        await db.SaveChangesAsync(ct);
        db.Products.Add(Product.Create(sku, "Two", Money.Of(1m), 1, Now));

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        exception.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task OnlyOneActivePayment_PerOrder_IsAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = OrderId.New();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

        var failed = Payment.Create(orderId, Money.Of(10m), "psp", Now);
        failed.Fail("card_declined", Now);
        var active = Payment.Create(orderId, Money.Of(10m), "psp", Now);
        db.Payments.AddRange(failed, active);
        await db.SaveChangesAsync(ct); // a failed payment does not block a new one

        db.Payments.Add(Payment.Create(orderId, Money.Of(10m), "psp", Now));
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("ux_payments_active_per_order");
    }

    [Fact]
    public async Task OnlyOneActiveDelivery_PerOrder_IsAllowed()
    {
        var ct = TestContext.Current.CancellationToken;
        var orderId = OrderId.New();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

        var quote = DeliveryQuote.Create(orderId, "sim", "dqt_" + Guid.NewGuid().ToString("N"), Money.Of(5m), Now.AddMinutes(30), 20, 5, Now.AddMinutes(15), Now);
        var cancelled = Delivery.Request(quote, 1, Now);
        cancelled.ConfirmCreated("del_" + Guid.NewGuid().ToString("N"), null, Money.Of(5m), Now);
        cancelled.Cancel(Now);
        var active = Delivery.Request(quote, 2, Now);
        db.AddRange(quote, cancelled, active);
        await db.SaveChangesAsync(ct);

        db.Deliveries.Add(Delivery.Request(quote, 3, Now));
        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));

        exception.InnerException.ShouldBeOfType<PostgresException>().ConstraintName.ShouldBe("ux_deliveries_active_per_order");
    }

    [Fact]
    public async Task ConcurrentStockReservations_SecondWriterGetsConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = NewProduct(stock: 1);
        await using (var setup = api.Services.CreateAsyncScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            db.Products.Add(product);
            await db.SaveChangesAsync(ct);
        }

        // Two independent units of work read the same row (xmin) before either writes.
        await using var scopeA = api.Services.CreateAsyncScope();
        await using var scopeB = api.Services.CreateAsyncScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var productA = await dbA.Products.SingleAsync(p => p.Id == product.Id, ct);
        var productB = await dbB.Products.SingleAsync(p => p.Id == product.Id, ct);

        productA.Reserve(1, Now);
        await dbA.SaveChangesAsync(ct);

        productB.Reserve(1, Now); // passes in memory: B still believes stock is 1
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => dbB.SaveChangesAsync(ct));

        await using var verify = api.Services.CreateAsyncScope();
        var stock = await verify.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>()
            .Products.AsNoTracking().Where(p => p.Id == product.Id).Select(p => p.StockQuantity).SingleAsync(ct);
        stock.ShouldBe(0, "exactly one reservation must win");
    }

    private static Product NewProduct(int stock) =>
        Product.Create("SKU-" + Guid.NewGuid().ToString("N")[..12], "Test product", Money.Of(10m), stock, Now);
}
