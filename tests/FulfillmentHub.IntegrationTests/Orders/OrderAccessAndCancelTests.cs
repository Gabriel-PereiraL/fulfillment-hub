using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Catalog;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;

namespace FulfillmentHub.IntegrationTests.Orders;

[Collection(ApiTests.Name)]
public sealed class OrderAccessAndCancelTests(ApiFixture api)
{
    [Fact]
    public async Task Customer_CannotSeeOrCancel_AnotherCustomersOrder()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var owner = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var other = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var order = await PlaceAsync(owner, product.Id.Value);

        var get = await other.GetAsync($"/api/v1/orders/{order.Id}", TestContext.Current.CancellationToken);
        var cancel = await other.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new { }, TestContext.Current.CancellationToken);
        var list = await other.GetFromJsonAsync<OrderPage>("/api/v1/orders", TestContext.Current.CancellationToken);

        get.StatusCode.ShouldBe(HttpStatusCode.NotFound, "existence of foreign orders is not revealed");
        cancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        list!.Items.ShouldNotContain(o => o.Id == order.Id);
        (await GetStockAsync(api, product.Id)).ShouldBe(4, "the foreign cancel attempt must not release stock");
    }

    [Fact]
    public async Task Operator_SeesEveryOrder_AndCancelsWithOperatorAction()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var operatorClient = await api.CreateAuthenticatedClientAsync([Role.Operator]);
        var order = await PlaceAsync(customer, product.Id.Value);

        var fetched = await operatorClient.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", TestContext.Current.CancellationToken);
        var cancelled = await operatorClient.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new { note = "customer called support" }, TestContext.Current.CancellationToken);

        fetched.ShouldNotBeNull().Id.ShouldBe(order.Id);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await cancelled.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken);
        body!.Status.ShouldBe("Cancelled");
        body.CancellationReason.ShouldBe("OperatorAction");
        body.StatusHistory[^1].Reason.ShouldBe("customer called support");
        (await GetStockAsync(api, product.Id)).ShouldBe(5, "cancelling returns the reserved stock");
    }

    [Fact]
    public async Task Customer_CancelsOwnOrder_Idempotently()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var order = await PlaceAsync(customer, product.Id.Value);

        var first = await customer.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new { }, TestContext.Current.CancellationToken);
        var second = await customer.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new { }, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await second.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!.CancellationReason.ShouldBe("CustomerRequest");
        (await GetStockAsync(api, product.Id)).ShouldBe(5, "stock is released exactly once");
    }

    [Fact]
    public async Task Customer_CannotCancel_OnceTheCourierHasTheParcel()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var order = await PlaceAsync(customer, product.Id.Value);
        await DriveToInDeliveryAsync(order.Id);

        var response = await customer.PostAsJsonAsync($"/api/v1/orders/{order.Id}/cancel", new { }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!
            .Extensions["code"]!.ToString().ShouldBe("order.cannot_cancel");
        (await GetStockAsync(api, product.Id)).ShouldBe(4);
    }

    [Fact]
    public async Task ListOrders_PagesNewestFirst_WithOpaqueCursor()
    {
        var product = await CreateProductAsync(api, stock: 10);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var first = await PlaceAsync(customer, product.Id.Value);
        var second = await PlaceAsync(customer, product.Id.Value);
        var third = await PlaceAsync(customer, product.Id.Value);

        var page1 = await customer.GetFromJsonAsync<OrderPage>("/api/v1/orders?pageSize=2", TestContext.Current.CancellationToken);
        page1!.Items.Select(o => o.Id).ShouldBe([third.Id, second.Id]);
        page1.NextCursor.ShouldNotBeNull();

        var page2 = await customer.GetFromJsonAsync<OrderPage>($"/api/v1/orders?pageSize=2&cursor={page1.NextCursor}", TestContext.Current.CancellationToken);
        page2!.Items.Select(o => o.Id).ShouldBe([first.Id]);
        page2.NextCursor.ShouldBeNull();

        var invalid = await customer.GetAsync("/api/v1/orders?cursor=not-a-cursor!", TestContext.Current.CancellationToken);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Products_ListsActiveProductsWithStock()
    {
        var product = await CreateProductAsync(api, stock: 7, price: 12.5m);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var products = await customer.GetFromJsonAsync<List<ProductDto>>("/api/v1/products", TestContext.Current.CancellationToken);

        var listed = products!.Single(p => p.Id == product.Id.Value);
        listed.StockQuantity.ShouldBe(7);
        listed.UnitPrice.Amount.ShouldBe(12.5m);
    }

    private static async Task<OrderDto> PlaceAsync(HttpClient client, Guid productId)
    {
        var response = await PlaceOrderAsync(client, OrderBody((productId, 1)), Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Simulates payment and pickup (later phases drive this through providers).</summary>
    private async Task DriveToInDeliveryAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var id = OrderId.From(orderId);
        var order = await db.Orders.SingleAsync(o => o.Id == id);
        var now = DateTimeOffset.UtcNow;
        var paymentId = PaymentId.New();
        order.MarkAwaitingPayment(paymentId, now);
        order.MarkAsPaid(paymentId, now);
        order.MarkDeliveryRequested(DeliveryId.New(), FulfillmentHub.Domain.Common.Money.Of(5m), now);
        order.MarkInDelivery(now);
        await db.SaveChangesAsync();
    }
}
