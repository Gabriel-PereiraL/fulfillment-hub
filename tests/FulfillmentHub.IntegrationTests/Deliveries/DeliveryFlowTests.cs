using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Deliveries;

/// <summary>
/// Delivery flow against the in-process simulator: quote at checkout (D-51), delivery requested after payment
/// (D-52), requote on expiry (T11), <c>409 duplicate_delivery</c> reconciled, cancellation at the provider.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class DeliveryFlowTests(ApiFixture api)
{
    private const string UndeliverablePostalCode = "00000-000";
    private const string ShortLivedQuotePostalCode = "50000-001";
    private const string SilentDeliveryPostalCode = "50000-003"; // sandbox: the provider sends no webhooks

    [Fact]
    public async Task PlaceOrder_QuotesTheDeliveryAtCheckout_AndChargesTheFee()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        order.DeliveryFee.ShouldNotBeNull().Amount.ShouldBeGreaterThan(0m);
        order.Total.Amount.ShouldBe(order.Subtotal.Amount + order.DeliveryFee.Amount);
        (await GetQuotesAsync(order.Id)).ShouldHaveSingleItem().Fee.Amount.ShouldBe(order.DeliveryFee.Amount);
        (await GetPaymentForOrderAsync(api, order.Id)).Amount.Amount.ShouldBe(order.Total.Amount, "the customer is charged products + delivery");
    }

    [Fact]
    public async Task PlaceOrder_WhenTheProviderIsDown_ChargesTheEstimatedFee_AndStillPlacesTheOrder()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        api.ProviderOutage.Enabled = true;
        HttpResponseMessage response;
        try
        {
            response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        }
        finally
        {
            api.ProviderOutage.Enabled = false;
        }

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        order.DeliveryFee.ShouldNotBeNull().Amount.ShouldBe(15m, "Fulfillment:Origin:EstimatedDeliveryFee");
        (await GetQuotesAsync(order.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task PlaceOrder_ToAnUndeliverableAddress_Returns400_AndReservesNothing()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody(UndeliverablePostalCode, (product.Id.Value, 1)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
        problem!.Extensions["code"]!.ToString().ShouldBe("order.address_undeliverable");
        (await GetStockAsync(api, product.Id)).ShouldBe(5);
    }

    [Fact]
    public async Task PaidOrder_GetsADeliveryRequested_ReusingTheCheckoutQuote()
    {
        var (client, placed) = await PlacePaidOrderAsync(ApprovedPrice);
        using (client)
        {
            var summary = await RunPendingDeliveriesAsync();

            summary.Requested.ShouldBeGreaterThanOrEqualTo(1);
            var order = await WaitForOrderStatusAsync(client, placed.Id, "DeliveryRequested");
            order.DeliveryId.ShouldNotBeNull();
            order.Total.Amount.ShouldBe(placed.Total.Amount, "the fee charged at checkout does not change");

            var delivery = await GetDeliveryAsync(order.DeliveryId.Value);
            delivery.Status.ShouldBeOneOf(DeliveryStatus.Pending, DeliveryStatus.Pickup); // the courier webhook may already be in
            delivery.ProviderDeliveryId.ShouldStartWith("del_");
            delivery.TrackingUrl.ShouldNotBeNullOrWhiteSpace();
            delivery.QuoteId.ShouldBe((await GetQuotesAsync(placed.Id)).Single().Id, "the checkout quote was still valid");
        }
    }

    [Fact]
    public async Task RequestDelivery_WhenTheCheckoutQuoteExpired_RequotesOnce()
    {
        // Sandbox rule: zip …-001 → the simulator issues 1-second quotes.
        var (client, placed) = await PlacePaidOrderAsync(ApprovedPrice, ShortLivedQuotePostalCode);
        using (client)
        {
            await Task.Delay(1200, TestContext.Current.CancellationToken);

            var result = await RequestDeliveryAsync(placed.Id);

            result.IsSuccess.ShouldBeTrue(result.IsSuccess ? null : result.Failure.Message);
            result.Value.ShouldBe(DeliveryRequestOutcome.Requested);
            var quotes = await GetQuotesAsync(placed.Id);
            quotes.Count.ShouldBe(2, "checkout quote + one requote");
            var order = await GetOrderAggregateAsync(api, placed.Id);
            order.Status.ShouldBe(OrderStatus.DeliveryRequested);
            (await GetDeliveryAsync(order.DeliveryId!.Value.Value)).QuoteId.ShouldBe(quotes.OrderByDescending(q => q.CreatedAt).First().Id);
        }
    }

    [Fact]
    public async Task RequestDelivery_WhenTheQuoteExpiresTwice_GivesUp_ForAnOperator()
    {
        var (client, placed) = await PlacePaidOrderAsync(ApprovedPrice);
        using (client)
        {
            api.ProviderOutage.Script = request =>
                request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/deliveries", StringComparison.Ordinal)
                    ? ProviderOutage.ProviderError(HttpStatusCode.BadRequest, "expired_quote")
                    : null;
            try
            {
                var result = await RequestDeliveryAsync(placed.Id);

                result.IsSuccess.ShouldBeFalse();
                result.Failure.Code.ShouldBe("delivery.quote_expired");
            }
            finally
            {
                api.ProviderOutage.Script = null;
            }

            (await GetQuotesAsync(placed.Id)).Count.ShouldBe(2);
            var order = await GetOrderAggregateAsync(api, placed.Id);
            order.Status.ShouldBe(OrderStatus.Paid, "left for operator attention, never cancelled by a transient problem");
            var deliveries = await GetDeliveriesForOrderAsync(placed.Id);
            deliveries.Count(d => d.Status == DeliveryStatus.Cancelled).ShouldBe(1, "the first attempt was superseded");
            deliveries.Count(d => d.Status == DeliveryStatus.Requested).ShouldBe(1, "the last attempt keeps its idempotency key for the next run");
            deliveries.ShouldAllBe(d => d.ProviderDeliveryId == null);
        }
    }

    [Fact]
    public async Task RequestDelivery_WhenTheProviderAlreadyHasIt_AdoptsTheExistingDelivery()
    {
        var (client, placed) = await PlacePaidOrderAsync(ApprovedPrice);
        using (client)
        {
            // Simulate "our first call timed out after the provider created it": create it directly with the key we will send.
            var existingId = await CreateAtProviderAsync($"order-{placed.Id:N}-delivery-1", placed);

            var result = await RequestDeliveryAsync(placed.Id);

            result.IsSuccess.ShouldBeTrue(result.IsSuccess ? null : result.Failure.Message);
            result.Value.ShouldBe(DeliveryRequestOutcome.Adopted);
            var order = await GetOrderAggregateAsync(api, placed.Id);
            order.Status.ShouldBe(OrderStatus.DeliveryRequested);
            (await GetDeliveryAsync(order.DeliveryId!.Value.Value)).ProviderDeliveryId.ShouldBe(existingId);
        }
    }

    [Fact]
    public async Task RequestDelivery_ForAnUndeliverableAddress_CancelsTheOrder_AndReleasesStock()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 2)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");

        api.ProviderOutage.Script = request =>
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/deliveries", StringComparison.Ordinal)
                ? ProviderOutage.ProviderError(HttpStatusCode.BadRequest, "address_undeliverable")
                : null;
        try
        {
            var result = await RequestDeliveryAsync(placed.Id);

            result.IsSuccess.ShouldBeFalse();
            result.Failure.Code.ShouldBe("provider.address_undeliverable");
        }
        finally
        {
            api.ProviderOutage.Script = null;
        }

        var order = await GetOrderAggregateAsync(api, placed.Id);
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe(OrderCancellationReason.DeliveryFailed);
        (await GetStockAsync(api, product.Id)).ShouldBe(5);
    }

    [Fact]
    public async Task CancelOrder_WithAnActiveDelivery_CancelsItAtTheProvider()
    {
        var (client, placed) = await PlacePaidOrderAsync(ApprovedPrice);
        using (client)
        {
            (await RequestDeliveryAsync(placed.Id)).IsSuccess.ShouldBeTrue();

            var cancel = await client.PostAsJsonAsync($"/api/v1/orders/{placed.Id}/cancel", new { note = "changed my mind" }, TestContext.Current.CancellationToken);

            cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
            var order = await GetOrderAggregateAsync(api, placed.Id);
            order.Status.ShouldBe(OrderStatus.Cancelled);
            var delivery = await GetDeliveryAsync(order.DeliveryId!.Value.Value);
            delivery.Status.ShouldBe(DeliveryStatus.Cancelled);
            (await GetAtProviderAsync(delivery.ProviderDeliveryId!)).GetProperty("status").GetString().ShouldBe("canceled");
        }
    }

    [Fact]
    public async Task CancelOrder_OnceTheCourierHasTheParcel_IsRefusedByTheProvider()
    {
        // Silent delivery: no webhook moves the order to InDelivery, so the refusal comes from the provider itself.
        var (client, placed) = await PlacePaidOrderAsync(ApprovedPrice, SilentDeliveryPostalCode);
        using (client)
        {
            (await RequestDeliveryAsync(placed.Id)).IsSuccess.ShouldBeTrue();
            var delivery = await GetDeliveryAsync((await GetOrderAggregateAsync(api, placed.Id)).DeliveryId!.Value.Value);
            await WaitForProviderStatusAsync(delivery.ProviderDeliveryId!, "pickup_complete");

            var cancel = await client.PostAsJsonAsync($"/api/v1/orders/{placed.Id}/cancel", new { note = "too late" }, TestContext.Current.CancellationToken);

            cancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var problem = await cancel.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
            problem!.Extensions["code"]!.ToString().ShouldBe("order.delivery_in_progress");
            (await GetOrderAggregateAsync(api, placed.Id)).Status.ShouldBe(OrderStatus.DeliveryRequested);
        }
    }

    private async Task<(HttpClient Client, OrderDto Order)> PlacePaidOrderAsync(decimal price, string postalCode = "50000-000")
    {
        var product = await CreateProductAsync(api, stock: 5, price: price);
        var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody(postalCode, (product.Id.Value, 1)), Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
        return (client, placed);
    }

    private async Task<FulfillmentHub.Application.Common.Result<DeliveryRequestOutcome>> RequestDeliveryAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RequestDeliveryHandler>()
            .HandleAsync(new RequestDeliveryCommand(orderId), TestContext.Current.CancellationToken);
    }

    private async Task<DeliveryRequestSummary> RunPendingDeliveriesAsync()
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RequestPendingDeliveriesHandler>().HandleAsync(100, TestContext.Current.CancellationToken);
    }

    private async Task<List<DeliveryQuote>> GetQuotesAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.DeliveryQuotes.AsNoTracking().Where(q => q.OrderId == new OrderId(orderId)).ToListAsync();
    }

    private async Task<Delivery> GetDeliveryAsync(Guid deliveryId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == new DeliveryId(deliveryId));
    }

    private async Task<List<Delivery>> GetDeliveriesForOrderAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Deliveries.AsNoTracking().Where(d => d.OrderId == new OrderId(orderId)).ToListAsync();
    }

    private async Task<HttpClient> ProviderClientAsync()
    {
        var client = api.Simulator.CreateClient();
        var token = await client.PostAsync("/delivery/oauth/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ProviderSimulatorFactory.DeliveryClientId,
            ["client_secret"] = ProviderSimulatorFactory.DeliveryClientSecret,
            ["grant_type"] = "client_credentials",
        }), TestContext.Current.CancellationToken);
        token.EnsureSuccessStatusCode();
        var accessToken = (await token.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    private async Task<string> CreateAtProviderAsync(string idempotencyKey, OrderDto order)
    {
        using var client = await ProviderClientAsync();
        var address = """{"street_address":["Rua das Flores, 123"],"city":"Recife","state":"PE","zip_code":"50000-000","country":"BR"}""";
        var body = new
        {
            pickup_name = "Store",
            pickup_address = address,
            pickup_phone_number = "+5581999990001",
            dropoff_name = "Customer",
            dropoff_address = address,
            dropoff_phone_number = "+5581999990000",
            manifest_items = new[] { new { name = "Item", quantity = 1, size = "small", price = 1990 } },
            manifest_reference = $"FH-{order.Number}",
            idempotency_key = idempotencyKey,
            external_id = idempotencyKey,
        };

        var response = await client.PostAsJsonAsync($"/delivery/v1/customers/{ProviderSimulatorFactory.DeliveryCustomerId}/deliveries", body, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
    }

    private async Task<JsonElement> GetAtProviderAsync(string providerDeliveryId)
    {
        using var client = await ProviderClientAsync();
        return await client.GetFromJsonAsync<JsonElement>($"/delivery/v1/customers/{ProviderSimulatorFactory.DeliveryCustomerId}/deliveries/{providerDeliveryId}", TestContext.Current.CancellationToken);
    }

    private async Task WaitForProviderStatusAsync(string providerDeliveryId, string status)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if ((await GetAtProviderAsync(providerDeliveryId)).GetProperty("status").GetString() == status)
            {
                return;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Provider delivery {providerDeliveryId} never reached '{status}'.");
    }
}
