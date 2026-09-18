using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Orders.Events;
using FulfillmentHub.Infrastructure.Messaging;
using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Webhooks;
using FulfillmentHub.IntegrationTests.Fixtures;
using FulfillmentHub.Worker.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Messaging;

/// <summary>
/// ADR-005 with a real (LocalStack) SQS: the order flow crosses the queues end to end, a redelivered message has no
/// second effect (T14) and a poison message lands in the dead-letter queue after MaxReceiveCount receives (T15).
/// </summary>
[Collection(SqsTests.Name)]
public sealed class SqsMessagingTests(SqsApiFixture api)
{
    [Fact]
    public async Task Order_IsPaidAndShipped_ThroughTheQueues()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        placed.Status.ShouldBe("Created");

        // OrderPlaced → fh-domain-events → payment; provider webhook → fh-webhooks-inbound → Paid; OrderPaid → delivery.
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
        await WaitForOrderStatusAsync(client, placed.Id, "DeliveryRequested");

        var outboxMessages = await GetOutboxMessagesAsync(placed.Id);
        outboxMessages.Where(m => m.Type is nameof(OrderPlaced) or nameof(OrderPaid)).ShouldAllBe(m => m.Status == OutboxMessageStatus.Processed);
        var processed = await GetProcessedMessagesAsync(DomainEventsConsumer.Name);
        processed.ShouldContain(outboxMessages.Single(m => m.Type == nameof(OrderPlaced)).Id.ToString());
        processed.ShouldContain(outboxMessages.Single(m => m.Type == nameof(OrderPaid)).Id.ToString());
        (await GetWebhookEventsForProviderAsync("simulated-psp")).ShouldContain(e => e.Status == WebhookEventStatus.Processed);
    }

    [Fact]
    public async Task RedeliveredMessage_IsAcknowledged_WithoutASecondEffect()
    {
        // T14: the same domain event delivered twice (SQS is at-least-once) creates exactly one payment.
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;
        await WaitForOrderStatusAsync(client, placed.Id, "Paid");
        var orderPlaced = (await GetOutboxMessagesAsync(placed.Id)).Single(m => m.Type == nameof(OrderPlaced));

        var envelope = new MessageEnvelope(orderPlaced.Id.ToString(), orderPlaced.Type, orderPlaced.Payload, orderPlaced.OccurredAt, null, null);
        await api.Services.GetRequiredService<IMessagePublisher>().PublishAsync(Queues.DomainEvents, envelope, TestContext.Current.CancellationToken);
        await WaitUntilQueueIsDrainedAsync(Queues.DomainEvents);

        (await GetPaymentsCountAsync(placed.Id)).ShouldBe(1, "the consumer deduplicates by message id");
        (await GetProcessedMessagesAsync(DomainEventsConsumer.Name)).Count(id => id == orderPlaced.Id.ToString()).ShouldBe(1);
    }

    [Fact]
    public async Task PoisonMessage_LandsInTheDeadLetterQueue_AfterMaxReceiveCount()
    {
        // T15: an event nobody can deserialize is never acknowledged; the redrive policy moves it after 3 receives.
        var id = Guid.NewGuid().ToString();
        var poison = new MessageEnvelope(id, "NoSuchEvent", "{}", DateTimeOffset.UtcNow, null, null);
        var sqs = api.Services.GetRequiredService<IAmazonSQS>();
        var queues = api.Services.GetRequiredService<SqsQueueProvisioner>();
        var dlqUrl = await queues.GetDeadLetterQueueUrlAsync(Queues.DomainEvents, TestContext.Current.CancellationToken);
        var before = await CountAsync(sqs, dlqUrl);

        await api.Services.GetRequiredService<IMessagePublisher>().PublishAsync(Queues.DomainEvents, poison, TestContext.Current.CancellationToken);

        var deadLettered = await PollAsync(async () =>
        {
            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest { QueueUrl = dlqUrl, MaxNumberOfMessages = 10, WaitTimeSeconds = 1, VisibilityTimeout = 0 }, TestContext.Current.CancellationToken);
            return (received.Messages ?? []).FirstOrDefault(m => m.Body.Contains(id, StringComparison.Ordinal));
        }, message => message is not null, TimeSpan.FromSeconds(30));

        deadLettered.ShouldNotBeNull();
        (await CountAsync(sqs, dlqUrl)).ShouldBeGreaterThan(before);
        (await GetProcessedMessagesAsync(DomainEventsConsumer.Name)).ShouldNotContain(id, "a poison message never counts as processed");
    }

    private async Task<List<OutboxMessage>> GetOutboxMessagesAsync(Guid aggregateId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId == aggregateId).ToListAsync();
    }

    private async Task<List<string>> GetProcessedMessagesAsync(string consumer)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.ProcessedMessages.AsNoTracking().Where(m => m.Consumer == consumer).Select(m => m.MessageId).ToListAsync();
    }

    private async Task<int> GetPaymentsCountAsync(Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Payments.AsNoTracking().CountAsync(p => p.OrderId == new OrderId(orderId));
    }

    private async Task<List<WebhookEvent>> GetWebhookEventsForProviderAsync(string provider)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.WebhookEvents.AsNoTracking().Where(e => e.Provider == provider).ToListAsync();
    }

    private async Task WaitUntilQueueIsDrainedAsync(string logicalQueue)
    {
        var sqs = api.Services.GetRequiredService<IAmazonSQS>();
        var url = await api.Services.GetRequiredService<SqsQueueProvisioner>().GetQueueUrlAsync(logicalQueue, TestContext.Current.CancellationToken);
        await PollAsync(async () => await CountAsync(sqs, url, includeInFlight: true), count => count == 0, TimeSpan.FromSeconds(20));
        await Task.Delay(500, TestContext.Current.CancellationToken); // let the last handler commit
    }

    private static async Task<int> CountAsync(IAmazonSQS sqs, string queueUrl, bool includeInFlight = false)
    {
        var attributes = await sqs.GetQueueAttributesAsync(queueUrl, [QueueAttributeName.ApproximateNumberOfMessages, QueueAttributeName.ApproximateNumberOfMessagesNotVisible], TestContext.Current.CancellationToken);
        return attributes.ApproximateNumberOfMessages + (includeInFlight ? attributes.ApproximateNumberOfMessagesNotVisible : 0);
    }

    private static async Task<T> PollAsync<T>(Func<Task<T>> read, Func<T, bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        T last = default!;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await read();
            if (condition(last))
            {
                return last;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Condition not met within {timeout}; last value: {JsonSerializer.Serialize(last)}");
    }
}
