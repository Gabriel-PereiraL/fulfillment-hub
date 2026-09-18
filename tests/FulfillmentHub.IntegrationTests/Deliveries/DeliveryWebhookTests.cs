using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Webhooks;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Deliveries;

/// <summary>
/// Inbound <c>event.delivery_status</c> webhooks: the real round trip with the simulator (order → delivered / returned),
/// T7 (out of order), duplicates, forged signatures and reconciliation of lost webhooks.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class DeliveryWebhookTests(ApiFixture api)
{
    private const string WebhookPath = "/api/v1/webhooks/deliveries";
    private const string SignatureHeader = "X-Uber-Signature";
    private const string ReturnedPostalCode = "50000-002";
    private const string SilentPostalCode = "50000-003";
    private const string DeliveryProvider = "uber-like-simulator";

    [Fact]
    public async Task Order_IsDelivered_ByTheProvidersWebhooks_EndToEnd()
    {
        var (client, placed, pause) = await PlaceOrderWithDeliveryRequestedAsync("50000-000");
        using (client)
        using (pause)
        {
            var delivered = await WaitForOrderStatusAsync(client, placed.Id, "Delivered");

            var order = await GetOrderAggregateAsync(api, placed.Id);
            order.StatusHistory.Select(h => h.To).ShouldContain(OrderStatus.InDelivery);
            order.StatusHistory.Count(h => h.To == OrderStatus.Delivered).ShouldBe(1);
            var delivery = await GetDeliveryAsync(delivered.DeliveryId!.Value);
            delivery.Status.ShouldBe(DeliveryStatus.Delivered);
            delivery.Courier.ShouldNotBeNull().Name.ShouldNotBeNullOrWhiteSpace();
            delivery.Courier.PhoneMasked.ShouldNotBeNull().ShouldContain("*", customMessage: "courier phones are stored masked");
            delivery.Events.Where(e => e.Disposition == DeliveryEventDisposition.Applied).Select(e => e.ProviderStatus)
                .ShouldBe(["pickup", "pickup_complete", "dropoff", "delivered"]);
        }
    }

    [Fact]
    public async Task ReturnedParcel_CancelsTheOrder_AndReleasesStock()
    {
        var product = await CreateProductAsync(api, stock: 4, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody(ReturnedPostalCode, (product.Id.Value, 2)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;

        // Outbox all the way: OrderPlaced → payment → OrderPaid → delivery → provider returns the parcel → webhook.
        var cancelled = await WaitForOrderStatusAsync(client, placed.Id, "Cancelled");

        cancelled.CancellationReason.ShouldBe(nameof(OrderCancellationReason.DeliveryFailed));
        (await GetDeliveryAsync(cancelled.DeliveryId!.Value)).Status.ShouldBe(DeliveryStatus.Returned);
        (await GetStockAsync(api, product.Id)).ShouldBe(4, "the parcel came back, the units are sellable again");
    }

    [Fact]
    public async Task Webhooks_OutOfOrder_NeverRegressTheDelivery_ButAreRecorded()
    {
        // T7: "delivered" arrives before "pickup"; the late "pickup" is recorded as stale, not applied.
        var (client, placed, pause) = await PlaceOrderWithDeliveryRequestedAsync(SilentPostalCode);
        using (client)
        using (pause)
        {
            var delivery = await GetDeliveryAsync((await GetOrderAggregateAsync(api, placed.Id)).DeliveryId!.Value.Value);
            var now = DateTimeOffset.UtcNow;
            using var webhookClient = api.CreateClientWithOwnAddress();

            var deliveredEventId = "evt_t7_delivered_" + Guid.NewGuid().ToString("N")[..8];
            var first = await webhookClient.SendAsync(Signed(StatusEvent(deliveredEventId, delivery.ProviderDeliveryId!, "delivered", now)), TestContext.Current.CancellationToken);
            var late = await webhookClient.SendAsync(Signed(StatusEvent("evt_t7_pickup_" + Guid.NewGuid().ToString("N")[..8], delivery.ProviderDeliveryId!, "pickup", now.AddMinutes(-5))), TestContext.Current.CancellationToken);
            var duplicate = await webhookClient.SendAsync(Signed(StatusEvent(deliveredEventId, delivery.ProviderDeliveryId!, "delivered", now)), TestContext.Current.CancellationToken);

            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            late.StatusCode.ShouldBe(HttpStatusCode.OK);
            duplicate.StatusCode.ShouldBe(HttpStatusCode.OK);

            var order = await GetOrderAggregateAsync(api, placed.Id);
            order.Status.ShouldBe(OrderStatus.Delivered);
            order.StatusHistory.Count(h => h.To == OrderStatus.Delivered).ShouldBe(1, "the duplicate had no effect");
            delivery = await GetDeliveryAsync(delivery.Id.Value);
            delivery.Status.ShouldBe(DeliveryStatus.Delivered, "a later event never regresses the status");
            delivery.Events.Select(e => (e.ProviderStatus, e.Disposition)).ShouldBe([
                ("delivered", DeliveryEventDisposition.Applied),
                ("pickup", DeliveryEventDisposition.Stale),
            ]);
            (await GetWebhookEventsAsync(api, deliveredEventId, DeliveryProvider)).ShouldHaveSingleItem().Status.ShouldBe(WebhookEventStatus.Processed);
        }
    }

    [Fact]
    public async Task Webhook_WithBadSignature_OrWrongHeader_IsRejected()
    {
        using var webhookClient = api.CreateClientWithOwnAddress();
        var eventId = "evt_bad_" + Guid.NewGuid().ToString("N")[..8];
        var body = StatusEvent(eventId, "del_whatever", "delivered", DateTimeOffset.UtcNow);

        var wrongKey = await webhookClient.SendAsync(Signed(body, signingKey: ProviderSimulatorFactory.WebhookSigningKey), TestContext.Current.CancellationToken);
        var paymentHeader = await webhookClient.SendAsync(Signed(body, signatureHeader: "X-Signature"), TestContext.Current.CancellationToken);
        var stale = await webhookClient.SendAsync(Signed(body, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10)), TestContext.Current.CancellationToken);

        wrongKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the payment provider's key must not sign delivery events");
        paymentHeader.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetWebhookEventsAsync(api, eventId, DeliveryProvider)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_ForUnknownDelivery_IsAcknowledged_AndIgnored()
    {
        using var webhookClient = api.CreateClientWithOwnAddress();
        var eventId = "evt_unknown_" + Guid.NewGuid().ToString("N")[..8];

        var response = await webhookClient.SendAsync(Signed(StatusEvent(eventId, "del_unknown", "delivered", DateTimeOffset.UtcNow)), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = (await GetWebhookEventsAsync(api, eventId, DeliveryProvider)).ShouldHaveSingleItem();
        stored.Provider.ShouldBe("uber-like-simulator");
        stored.Status.ShouldBe(WebhookEventStatus.Ignored);
    }

    [Fact]
    public async Task LostWebhooks_AreRecoveredByReconciliation()
    {
        var (client, placed, pause) = await PlaceOrderWithDeliveryRequestedAsync(SilentPostalCode);
        using (client)
        using (pause)
        {
            await Task.Delay(2500, TestContext.Current.CancellationToken); // the provider delivers, silently
            (await client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{placed.Id}", TestContext.Current.CancellationToken))!
                .Status.ShouldBe("DeliveryRequested", "no webhook arrived");

            using var scope = api.Services.CreateScope();
            var summary = await scope.ServiceProvider.GetRequiredService<ReconcileDeliveriesHandler>()
                .HandleAsync(quietFor: TimeSpan.Zero, batchSize: 100, TestContext.Current.CancellationToken);

            summary.Corrected.ShouldBeGreaterThanOrEqualTo(1);
            var order = await WaitForOrderStatusAsync(client, placed.Id, "Delivered");
            var delivery = await GetDeliveryAsync(order.DeliveryId!.Value);
            delivery.Status.ShouldBe(DeliveryStatus.Delivered);
            delivery.Events.ShouldHaveSingleItem().ProviderEventId.ShouldStartWith("reconciled:");
        }
    }

    /// <summary>Outbox paused: payment published by hand, delivery requested by hand, so the test controls what the provider sees.</summary>
    private async Task<(HttpClient Client, OrderDto Order, IDisposable OutboxPause)> PlaceOrderWithDeliveryRequestedAsync(string postalCode)
    {
        var pause = api.Outbox.Pause();
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody(postalCode, (product.Id.Value, 1)), Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        await api.Outbox.RunOnceAsync();
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
        (await RequestDeliveryAsync(placed.Id)).IsSuccess.ShouldBeTrue();
        return (client, placed, pause);
    }

    private async Task<FulfillmentHub.Application.Common.Result<DeliveryRequestOutcome>> RequestDeliveryAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RequestDeliveryHandler>()
            .HandleAsync(new RequestDeliveryCommand(orderId), TestContext.Current.CancellationToken);
    }

    private async Task<Delivery> GetDeliveryAsync(Guid deliveryId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Deliveries.AsNoTracking().Include(d => d.Events).SingleAsync(d => d.Id == new DeliveryId(deliveryId));
    }

    private static HttpRequestMessage Signed(
        string body,
        string signingKey = ProviderSimulatorFactory.DeliveryWebhookSigningKey,
        DateTimeOffset? timestamp = null,
        string signatureHeader = SignatureHeader) =>
        SignedWebhook(body, signingKey, timestamp, path: WebhookPath, signatureHeader: signatureHeader);

    /// <summary>The documented subset of <c>event.delivery_status</c>, as the simulator sends it.</summary>
    private static string StatusEvent(string eventId, string providerDeliveryId, string status, DateTimeOffset updated)
    {
        var stamp = updated.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return $"{{\"id\":\"{eventId}\",\"kind\":\"event.delivery_status\",\"created\":\"{stamp}\",\"status\":\"{status}\",\"delivery_id\":\"{providerDeliveryId}\",\"customer_id\":\"cus_sim_tests\",\"live_mode\":false,"
            + $"\"data\":{{\"id\":\"{providerDeliveryId}\",\"status\":\"{status}\",\"updated\":\"{stamp}\",\"courier\":{{\"name\":\"Ana\",\"vehicle_type\":\"bicycle\",\"phone_number\":\"+5581912345678\",\"location\":{{\"lat\":-8.05,\"lng\":-34.9}}}},\"tracking_url\":\"https://simulator.local/track/{providerDeliveryId}\"}}}}";
    }
}
