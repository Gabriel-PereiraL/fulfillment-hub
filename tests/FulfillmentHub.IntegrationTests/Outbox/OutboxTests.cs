using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Api.Admin;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Orders.Events;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Outbox;

/// <summary>
/// ADR-004 guarantees: the event is committed with the aggregate (T12), a failing handler is retried with backoff and
/// parked as Failed after the budget, an administrator can requeue it (T13), and consumers are idempotent.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class OutboxTests(ApiFixture api)
{
    [Fact]
    public async Task OrderPlaced_IsCommittedWithTheOrder_AndPublishedByTheWorker()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        OrderDto placed;
        OutboxMessage message;
        using (api.Outbox.Pause())
        {
            var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;

            // Same commit as the order: the row is there before any publisher ran, with the request's correlation id.
            message = (await GetMessagesAsync(placed.Id)).ShouldHaveSingleItem();
            message.Type.ShouldBe(nameof(OrderPlaced));
            message.Status.ShouldBe(OutboxMessageStatus.Pending);
            message.Attempts.ShouldBe(0);
            message.CorrelationId.ShouldNotBeNullOrWhiteSpace();
            message.Payload.ShouldContain(placed.Id.ToString());
            (await GetPaymentsAsync(placed.Id)).ShouldBeEmpty("no side effect inside the request");
        }

        await WaitForOrderStatusAsync(client, placed.Id, "Paid");

        var published = await GetMessageAsync(message.Id);
        published.Status.ShouldBe(OutboxMessageStatus.Processed);
        published.ProcessedAt.ShouldNotBeNull();
        (await GetMessagesAsync(placed.Id)).Select(m => m.Type).ShouldContain(nameof(OrderPaid), "the webhook transaction raised the next event");
    }

    [Fact]
    public async Task PublishFailure_AfterTheCommit_IsRetried_UntilTheHandlerSucceeds()
    {
        // T12: the "crash" happens between the commit and the effect: the provider is down when the message is first
        // published. The order is durable, the message stays pending with a retry schedule, and later succeeds.
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var pause = api.Outbox.Pause();
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;

        api.ProviderOutage.Enabled = true;
        OutboxBatchSummary firstPass;
        try
        {
            firstPass = await api.Outbox.RunOnceAsync();
        }
        finally
        {
            api.ProviderOutage.Enabled = false;
        }

        firstPass.Retried.ShouldBe(1);
        var message = (await GetMessagesAsync(placed.Id)).ShouldHaveSingleItem();
        message.Status.ShouldBe(OutboxMessageStatus.Pending);
        message.Attempts.ShouldBe(1);
        message.LastError.ShouldNotBeNull().ShouldContain("unavailable"); // provider.service_unavailable (503) or provider.unavailable (transport/circuit)
        message.NextAttemptAt.ShouldBeGreaterThan(message.OccurredAt);
        (await client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{placed.Id}", TestContext.Current.CancellationToken))!.Status.ShouldBe("Created");

        var secondPass = await RunUntilAsync(() => GetMessageAsync(message.Id), m => m.Status == OutboxMessageStatus.Processed);

        secondPass.Status.ShouldBe(OutboxMessageStatus.Processed);
        secondPass.Attempts.ShouldBe(1, "the successful attempt is not counted as a failure");
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
    }

    [Fact]
    public async Task HandlerFailingRepeatedly_ParksTheMessageAsFailed_AndAnAdministratorCanRequeueIt()
    {
        // T13: Outbox:MaxAttempts is 3 in the test host.
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var admin = await api.CreateAuthenticatedClientAsync([Role.Admin]);
        using var pause = api.Outbox.Pause();
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        var message = (await GetMessagesAsync(placed.Id)).ShouldHaveSingleItem();

        api.ProviderOutage.Enabled = true;
        OutboxMessage failed;
        try
        {
            failed = await RunUntilAsync(() => GetMessageAsync(message.Id), m => m.Status == OutboxMessageStatus.Failed);
        }
        finally
        {
            api.ProviderOutage.Enabled = false;
        }

        failed.Attempts.ShouldBe(3);
        failed.LastError.ShouldNotBeNull().ShouldContain("unavailable");

        var listed = await admin.GetFromJsonAsync<List<OutboxMessageDto>>("/api/v1/admin/outbox?status=Failed", TestContext.Current.CancellationToken);
        listed!.ShouldContain(m => m.Id == message.Id);

        var retry = await admin.PostAsync($"/api/v1/admin/outbox/{message.Id}/retry", content: null, TestContext.Current.CancellationToken);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
        var requeued = (await retry.Content.ReadFromJsonAsync<OutboxMessageDto>(TestContext.Current.CancellationToken))!;
        requeued.Status.ShouldBe(nameof(OutboxMessageStatus.Pending));
        requeued.Attempts.ShouldBe(0);

        // The provider circuit may still be open from the failures above (1 s in tests): keep publishing until it closes.
        (await RunUntilAsync(() => GetMessageAsync(message.Id), m => m.Status == OutboxMessageStatus.Processed)).Status.ShouldBe(OutboxMessageStatus.Processed);
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
    }

    [Fact]
    public async Task OutboxAdmin_RequiresTheAdminRole()
    {
        using var customer = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var operatorClient = await api.CreateAuthenticatedClientAsync([Role.Operator]);

        (await customer.GetAsync("/api/v1/admin/outbox", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await operatorClient.PostAsync($"/api/v1/admin/outbox/{Guid.NewGuid()}/retry", content: null, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RedeliveringAProcessedMessage_HasNoSecondEffect()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var admin = await api.CreateAuthenticatedClientAsync([Role.Admin]);
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
        var orderPlaced = (await GetMessagesAsync(placed.Id)).Single(m => m.Type == nameof(OrderPlaced));

        // At-least-once: pretend the mark was lost after the effect and the message is delivered again.
        (await admin.PostAsync($"/api/v1/admin/outbox/{orderPlaced.Id}/retry", content: null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        await RunUntilAsync(() => GetMessageAsync(orderPlaced.Id), m => m.Status == OutboxMessageStatus.Processed);

        (await GetPaymentsAsync(placed.Id)).ShouldHaveSingleItem("the handler is idempotent");
    }

    [Fact]
    public async Task CancellingAPaidOrder_RefundsThePayment_ThroughTheOutbox()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        var paid = await WaitForOrderStatusAsync(client, placed.Id, "Paid");

        var cancel = await client.PostAsJsonAsync($"/api/v1/orders/{placed.Id}/cancel", new { note = "changed my mind" }, TestContext.Current.CancellationToken);

        cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payment = await WaitForPaymentStatusAsync(api, paid.PaymentId!.Value, PaymentStatus.Refunded);
        payment.Status.ShouldBe(PaymentStatus.Refunded);
        var cancelledMessage = (await GetMessagesAsync(placed.Id)).Single(m => m.Type == nameof(OrderCancelled));
        (await WaitForMessageAsync(cancelledMessage.Id, m => m.Status == OutboxMessageStatus.Processed)).Status.ShouldBe(OutboxMessageStatus.Processed);
        (await GetStockAsync(api, product.Id)).ShouldBe(5);
    }

    private async Task<List<OutboxMessage>> GetMessagesAsync(Guid aggregateId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId == aggregateId).OrderBy(m => m.OccurredAt).ToListAsync();
    }

    private async Task<OutboxMessage> GetMessageAsync(Guid id)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private async Task<List<Payment>> GetPaymentsAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Payments.AsNoTracking().Where(p => p.OrderId == new OrderId(orderId)).ToListAsync();
    }

    /// <summary>Waits for the background publisher (not paused) to bring the message to the expected state.</summary>
    private async Task<OutboxMessage> WaitForMessageAsync(Guid id, Func<OutboxMessage, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var message = await GetMessageAsync(id);
            if (condition(message))
            {
                return message;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The outbox message did not reach the expected state.");
    }

    /// <summary>Runs publisher passes (the message backs off between attempts) until the condition holds, or fails after 15 s.</summary>
    private async Task<OutboxMessage> RunUntilAsync(Func<Task<OutboxMessage>> read, Func<OutboxMessage, bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await api.Outbox.RunOnceAsync();
            var message = await read();
            if (condition(message))
            {
                return message;
            }

            await Task.Delay(150, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The outbox message did not reach the expected state.");
    }
}
