using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;

namespace FulfillmentHub.IntegrationTests.Orders;

[Collection(ApiTests.Name)]
public sealed class PlaceOrderTests(ApiFixture api)
{
    [Fact]
    public async Task PlaceOrder_CreatesOrder_ReservesStock_AndIsRetrievable()
    {
        var product = await CreateProductAsync(api, stock: 5, price: 19.9m);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 2)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken);
        order.ShouldNotBeNull();
        response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/orders/{order.Id}");
        order.Number.ShouldBeGreaterThanOrEqualTo(1000);
        order.Status.ShouldBe("Created", "the payment is created asynchronously from the outbox (ADR-004)");
        order.PaymentId.ShouldBeNull();
        order.Subtotal.Amount.ShouldBe(39.8m);
        order.DeliveryFee.ShouldNotBeNull("the delivery is quoted at checkout (D-51)");
        order.Total.Amount.ShouldBe(39.8m + order.DeliveryFee.Amount);
        order.Items.ShouldHaveSingleItem().Sku.ShouldBe(product.Sku);

        (await GetStockAsync(api, product.Id)).ShouldBe(3);

        var fetched = await client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{order.Id}", TestContext.Current.CancellationToken);
        fetched.ShouldNotBeNull().Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task PlaceOrder_WithUnreadableBody_Returns400Problem_NotA500()
    {
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var body = new { items = new[] { new { productId = "not-a-guid", quantity = 1 } }, deliveryAddress = new { street = "Rua A" } };

        var response = await PlaceOrderAsync(client, body, Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Title.ShouldBe("Malformed request");
        problem.Detail!.Contains("Exception", StringComparison.Ordinal).ShouldBeFalse("internals never leak to clients");
    }

    [Fact]
    public async Task PlaceOrder_WithoutIdempotencyKey_Returns400()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), idempotencyKey: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!
            .Extensions["code"]!.ToString().ShouldBe("idempotency.key_required");
        (await GetStockAsync(api, product.Id)).ShouldBe(5);
    }

    [Fact]
    public async Task PlaceOrder_RepeatedWithSameKeyAndPayload_ReplaysResponse_WithoutSecondOrder()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var key = Guid.NewGuid().ToString();
        var body = OrderBody((product.Id.Value, 1));

        var first = await PlaceOrderAsync(client, body, key);
        var second = await PlaceOrderAsync(client, body, key);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.TryGetValues("Idempotent-Replayed", out var replayed).ShouldBeTrue();
        replayed!.ShouldBe(["true"]);
        second.Headers.Location.ShouldBe(first.Headers.Location);

        var firstOrder = await first.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken);
        var secondOrder = await second.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken);
        secondOrder!.Id.ShouldBe(firstOrder!.Id);

        (await CountOrdersForProductAsync(api, product.Id)).ShouldBe(1);
        (await GetStockAsync(api, product.Id)).ShouldBe(4, "stock is reserved once, not per retry");
    }

    [Fact]
    public async Task PlaceOrder_RepeatedWithSameKeyButDifferentPayload_Returns422()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var key = Guid.NewGuid().ToString();

        (await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), key)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var mismatch = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 2)), key);

        mismatch.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await mismatch.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))!
            .Extensions["code"]!.ToString().ShouldBe("idempotency.payload_mismatch");
        (await CountOrdersForProductAsync(api, product.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task PlaceOrder_ConcurrentRequestsWithSameKey_CreateExactlyOneOrder()
    {
        var product = await CreateProductAsync(api, stock: 50);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var key = Guid.NewGuid().ToString();
        var body = OrderBody((product.Id.Value, 1));

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => PlaceOrderAsync(client, body, key)));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        statuses.ShouldAllBe(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        statuses.ShouldContain(HttpStatusCode.Created);

        var ids = new HashSet<Guid>();
        foreach (var response in responses.Where(r => r.StatusCode == HttpStatusCode.Created))
        {
            ids.Add((await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!.Id);
        }

        ids.Count.ShouldBe(1, "every 201 must refer to the same order");
        (await CountOrdersForProductAsync(api, product.Id)).ShouldBe(1);
        (await GetStockAsync(api, product.Id)).ShouldBe(49);
    }

    [Fact]
    public async Task PlaceOrder_TwentyBuyersForTheLastUnit_ExactlyOneSucceeds()
    {
        var product = await CreateProductAsync(api, stock: 1);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var body = OrderBody((product.Id.Value, 1));

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PlaceOrderAsync(client, body, Guid.NewGuid().ToString())));

        var statuses = responses.Select(r => r.StatusCode).ToList();
        statuses.Count(s => s == HttpStatusCode.Created).ShouldBe(1, "there is only one unit in stock");
        statuses.Where(s => s != HttpStatusCode.Created).ShouldAllBe(s => s == HttpStatusCode.Conflict);

        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var problem = await conflict.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
            problem!.Extensions["code"]!.ToString().ShouldBeOneOf("order.insufficient_stock", "order.stock_conflict");
        }

        (await GetStockAsync(api, product.Id)).ShouldBe(0, "stock must never go negative");
        (await CountOrdersForProductAsync(api, product.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task PlaceOrder_IgnoresServerControlledFields_InPayload()
    {
        var product = await CreateProductAsync(api, stock: 5, price: 10m);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var body = new
        {
            items = new[] { new { productId = product.Id.Value, quantity = 1, unitPrice = 0.01m } },
            deliveryAddress = new { street = "Rua A", number = "1", district = "B", city = "C", state = "PE", postalCode = "50000000" },
            status = "Paid",
            total = new { amount = 0.01m, currency = "BRL" },
            customerId = Guid.NewGuid(),
        };

        var response = await PlaceOrderAsync(client, body, Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken);
        order!.Status.ShouldBe("Created");
        order.Subtotal.Amount.ShouldBe(10m);
        order.Total.Amount.ShouldBe(10m + order.DeliveryFee!.Amount);
        order.CustomerId.ShouldNotBe((Guid)body.customerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task PlaceOrder_WithInvalidQuantity_Returns400(int quantity)
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, quantity)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task PlaceOrder_WithUnknownProduct_Returns404()
    {
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((Guid.NewGuid(), 1)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PlaceOrder_ByOperator_Returns403()
    {
        var product = await CreateProductAsync(api, stock: 5);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Operator]);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
