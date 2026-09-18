using System.Text.Json;
using Amazon.SQS;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Infrastructure.Messaging;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker.Messaging;

/// <summary>
/// Consumes <c>fh-webhooks-inbound</c>: each message points at a stored webhook event; processing is idempotent
/// because an event that is no longer <c>Received</c> is acknowledged without work.
/// </summary>
public sealed class WebhooksInboundConsumer(
    IAmazonSQS sqs,
    SqsQueueProvisioner queues,
    IOptions<SqsOptions> options,
    QueueMetrics metrics,
    WebhookEventProcessor processor,
    TimeProvider timeProvider,
    ILogger<WebhooksInboundConsumer> logger) : SqsConsumer(sqs, queues, options, metrics, timeProvider, logger)
{
    public const string Name = "webhooks-inbound";

    private sealed record WebhookInboundMessage(Guid WebhookEventId, string Provider);

    protected override string ConsumerName => Name;

    protected override string LogicalQueue => Queues.WebhooksInbound;

    protected override async Task<bool> HandleAsync(MessageEnvelope envelope, int receiveCount, CancellationToken cancellationToken)
    {
        WebhookInboundMessage? pointer;
        try
        {
            pointer = JsonSerializer.Deserialize<WebhookInboundMessage>(envelope.Payload, SqsMessagePublisher.JsonOptions);
        }
        catch (JsonException)
        {
            pointer = null;
        }

        if (pointer is null || pointer.WebhookEventId == Guid.Empty)
        {
            return false;
        }

        // A Failed outcome is recorded on the event itself and reconciliation catches up: redelivering the pointer
        // would not help, so the message is acknowledged whatever the outcome.
        await processor.ProcessAsync(pointer.WebhookEventId, cancellationToken);
        return true;
    }
}
