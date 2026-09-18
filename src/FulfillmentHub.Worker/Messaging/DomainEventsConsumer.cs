using Amazon.SQS;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Application.Outbox;
using FulfillmentHub.Infrastructure.Messaging;
using FulfillmentHub.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker.Messaging;

/// <summary>
/// Consumes <c>fh-domain-events</c>: runs the outbox handler of each event with persisted deduplication
/// (<c>processed_messages</c>), so a redelivered message never repeats its effect.
/// </summary>
public sealed class DomainEventsConsumer(
    IAmazonSQS sqs,
    SqsQueueProvisioner queues,
    IOptions<SqsOptions> options,
    QueueMetrics metrics,
    OutboxDispatcher dispatcher,
    TimeProvider timeProvider,
    ILogger<DomainEventsConsumer> logger) : SqsConsumer(sqs, queues, options, metrics, timeProvider, logger)
{
    public const string Name = "domain-events";

    protected override string ConsumerName => Name;

    protected override string LogicalQueue => Queues.DomainEvents;

    protected override async Task<bool> HandleAsync(MessageEnvelope envelope, int receiveCount, CancellationToken cancellationToken)
    {
        var handling = await dispatcher.DispatchAsync(envelope.Type, envelope.Payload, envelope.TraceParent, (Name, envelope.Id), cancellationToken);
        return handling is OutboxHandling.Done;
    }
}
