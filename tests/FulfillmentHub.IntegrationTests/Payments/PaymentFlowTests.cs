using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Webhooks;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Payments;

/// <summary>
/// End-to-end payment flow through the in-process simulator: order → provider → signed webhook → order state.
/// Covers TEST_STRATEGY T6 (duplicate webhook), T8 (forged webhook), T17 (declined payment) and T18 (reconciliation).
/// </summary>
[Collection(ApiTests.Name)]
public sealed class PaymentFlowTests(ApiFixture api)
{
    [Fact]
    public async Task PlaceOrder_InitiatesPayment_AndWebhookMarksOrderPaid()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        placed.Status.ShouldBe("Created", "the payment is created by the outbox publisher, not inside the request (ADR-004)");
        placed.PaymentId.ShouldBeNull();

        var paid = await WaitForOrderStatusAsync(client, placed.Id, "Paid");
        paid.PaymentId.ShouldNotBeNull();

        var payment = await GetPaymentAsync(api, paid.PaymentId.Value);
        payment.Status.ShouldBe(PaymentStatus.Paid);
        payment.Provider.ShouldBe(ProviderName);
        payment.ProviderPaymentId.ShouldNotBeNullOrWhiteSpace();
        payment.Attempts.ShouldHaveSingleItem().Outcome.ShouldBe(PaymentAttemptOutcome.Succeeded);

        var order = await GetOrderAggregateAsync(api, placed.Id);
        order.StatusHistory.Select(h => h.To).Take(3).ShouldBe([OrderStatus.Created, OrderStatus.AwaitingPayment, OrderStatus.Paid]); // the outbox carries on to the delivery
    }

    [Fact]
    public async Task DeclinedPayment_CancelsOrder_AndReleasesStock()
    {
        var product = await CreateProductAsync(api, stock: 3, price: DeclinedPrice); // 999 cents → declined by the sandbox rule
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        (await GetStockAsync(api, product.Id)).ShouldBe(2, "stock is reserved while the payment is pending");

        var cancelled = await WaitForOrderStatusAsync(client, placed.Id, "Cancelled");

        cancelled.CancellationReason.ShouldBe(nameof(OrderCancellationReason.PaymentFailed));
        (await GetStockAsync(api, product.Id)).ShouldBe(3, "a declined payment returns the reserved stock");
        var payment = await GetPaymentAsync(api, cancelled.PaymentId!.Value);
        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureReason.ShouldBe("card_declined");
    }

    [Fact]
    public async Task SilentSettlement_IsPickedUpByReconciliation()
    {
        var product = await CreateProductAsync(api, stock: 5, price: SilentlyApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var created = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        var placed = await WaitForOrderStatusAsync(client, created.Id, "AwaitingPayment");

        // The provider settles the payment but never calls back (lost webhook).
        await Task.Delay(600, TestContext.Current.CancellationToken);
        (await client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{placed.Id}", TestContext.Current.CancellationToken))!
            .Status.ShouldBe("AwaitingPayment", "without the webhook the order is stuck");

        using var scope = api.Services.CreateScope();
        var summary = await scope.ServiceProvider.GetRequiredService<ReconcilePaymentsHandler>()
            .HandleAsync(pendingFor: TimeSpan.Zero, batchSize: 100, TestContext.Current.CancellationToken);

        summary.Corrected.ShouldBeGreaterThanOrEqualTo(1);
        (await WaitForOrderStatusAsync(client, placed.Id, "Paid")).PaymentId.ShouldBe(placed.PaymentId);
        (await GetPaymentAsync(api, placed.PaymentId!.Value)).Status.ShouldBe(PaymentStatus.Paid);
    }

    [Fact]
    public async Task PaymentCapturedAfterCustomerCancelled_KeepsOrderCancelled_AndRefundsIt()
    {
        var product = await CreateProductAsync(api, stock: 5, price: SilentlyApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var created = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        var placed = await WaitForOrderStatusAsync(client, created.Id, "AwaitingPayment");

        var cancel = await client.PostAsJsonAsync($"/api/v1/orders/{placed.Id}/cancel", new { note = "changed my mind" }, TestContext.Current.CancellationToken);
        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        await Task.Delay(300, TestContext.Current.CancellationToken); // the provider settles it as paid regardless

        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ReconcilePaymentsHandler>()
            .HandleAsync(pendingFor: TimeSpan.Zero, batchSize: 100, TestContext.Current.CancellationToken);

        var order = await GetOrderAggregateAsync(api, placed.Id);
        order.Status.ShouldBe(OrderStatus.Cancelled, "a late capture never resurrects a cancelled order");
        (await GetStockAsync(api, product.Id)).ShouldBe(5);

        // PaymentPaid → outbox → refund (BL-244): the money goes back without anyone touching it.
        var refunded = await WaitForPaymentStatusAsync(api, placed.PaymentId!.Value, PaymentStatus.Refunded);
        refunded.Status.ShouldBe(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task DuplicateWebhook_IsAcknowledged_ButAppliedOnce()
    {
        var product = await CreateProductAsync(api, stock: 5, price: SilentlyApprovedPrice);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(customer, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var created = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        var placed = await WaitForOrderStatusAsync(customer, created.Id, "AwaitingPayment");
        var providerPaymentId = (await GetPaymentAsync(api, placed.PaymentId!.Value)).ProviderPaymentId!;
        await Task.Delay(300, TestContext.Current.CancellationToken); // let the provider settle (silently)

        var eventId = "evt_dup_" + Guid.NewGuid().ToString("N")[..12];
        var body = StatusChangedEvent(eventId, providerPaymentId, "paid");
        using var webhookClient = api.CreateClientWithOwnAddress();

        var first = await webhookClient.SendAsync(SignedWebhook(body), TestContext.Current.CancellationToken);
        var second = await webhookClient.SendAsync(SignedWebhook(body), TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, "providers retry until they get a 2xx; duplicates are acknowledged");
        var events = await GetWebhookEventsAsync(api, eventId);
        events.ShouldHaveSingleItem().Status.ShouldBe(WebhookEventStatus.Processed);
        var order = await GetOrderAggregateAsync(api, placed.Id);
        order.Status.ShouldBe(OrderStatus.Paid);
        order.StatusHistory.Count(h => h.To == OrderStatus.Paid).ShouldBe(1);
    }

    [Fact]
    public async Task Webhook_ClaimingPaid_IsVerifiedWithTheProvider_BeforeBeingTrusted()
    {
        // D-P5: a validly signed "paid" event for a payment the provider reports as failed must not mark the order paid.
        var product = await CreateProductAsync(api, stock: 5, price: DeclinedPrice);
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(customer, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var created = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        var placed = await WaitForOrderStatusAsync(customer, created.Id, "Cancelled");
        var providerPaymentId = (await GetPaymentAsync(api, placed.PaymentId!.Value)).ProviderPaymentId!;

        var eventId = "evt_forged_" + Guid.NewGuid().ToString("N")[..12];
        using var webhookClient = api.CreateClientWithOwnAddress();
        var result = await webhookClient.SendAsync(SignedWebhook(StatusChangedEvent(eventId, providerPaymentId, "paid")), TestContext.Current.CancellationToken);

        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetWebhookEventsAsync(api, eventId)).ShouldHaveSingleItem().Status.ShouldBe(WebhookEventStatus.Processed);
        (await GetPaymentAsync(api, placed.PaymentId.Value)).Status.ShouldBe(PaymentStatus.Failed, "the provider's own answer wins");
        (await GetOrderAggregateAsync(api, placed.Id)).Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Webhook_WithBadSignature_IsRejected_AndNothingIsPersisted()
    {
        using var webhookClient = api.CreateClientWithOwnAddress();
        var eventId = "evt_bad_" + Guid.NewGuid().ToString("N")[..12];
        var body = StatusChangedEvent(eventId, "pay_does_not_matter", "paid");

        var wrongKey = await webhookClient.SendAsync(SignedWebhook(body, signingKey: "some-other-key-of-16-chars"), TestContext.Current.CancellationToken);
        var stale = await webhookClient.SendAsync(SignedWebhook(body, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10)), TestContext.Current.CancellationToken);
        var missing = await webhookClient.SendAsync(SignedWebhook(body, includeSignature: false), TestContext.Current.CancellationToken);
        var tampered = await webhookClient.SendAsync(
            SignedWebhook(body.Replace("\"paid\"", "\"failed\"", StringComparison.Ordinal), signedBody: body),
            TestContext.Current.CancellationToken);

        wrongKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        missing.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        tampered.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await GetWebhookEventsAsync(api, eventId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_WithValidSignature_ButMalformedPayload_Returns400()
    {
        using var webhookClient = api.CreateClientWithOwnAddress();

        var notJson = await webhookClient.SendAsync(SignedWebhook("this is not json"), TestContext.Current.CancellationToken);
        var noData = await webhookClient.SendAsync(SignedWebhook("""{"id":"evt_x","type":"payment.status_changed"}"""), TestContext.Current.CancellationToken);

        notJson.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        noData.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_ForUnknownPayment_IsAcknowledged_AndRecordedAsIgnored()
    {
        using var webhookClient = api.CreateClientWithOwnAddress();
        var eventId = "evt_unknown_" + Guid.NewGuid().ToString("N")[..12];

        var result = await webhookClient.SendAsync(SignedWebhook(StatusChangedEvent(eventId, "pay_unknown", "paid")), TestContext.Current.CancellationToken);

        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = (await GetWebhookEventsAsync(api, eventId)).ShouldHaveSingleItem();
        stored.Status.ShouldNotBe(WebhookEventStatus.Processed);
    }
}
