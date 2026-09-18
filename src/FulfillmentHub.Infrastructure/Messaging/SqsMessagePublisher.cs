using System.Diagnostics;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Infrastructure.Messaging;

/// <summary>Publishes envelopes to SQS: JSON body plus <c>type</c>/<c>traceparent</c> message attributes for cheap filtering and trace continuity.</summary>
public sealed partial class SqsMessagePublisher(IAmazonSQS sqs, SqsQueueProvisioner queues, ILogger<SqsMessagePublisher> logger) : IMessagePublisher
{
    public const string TypeAttribute = "type";
    public const string TraceParentAttribute = "traceparent";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ActivitySource ActivitySource = new(TelemetryNames.ActivitySource);

    public bool IsEnabled => true;

    public async Task PublishAsync(string queue, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity($"{queue} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "aws_sqs");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.message.id", envelope.Id);

        var url = await queues.GetQueueUrlAsync(queue, cancellationToken);
        var request = new SendMessageRequest
        {
            QueueUrl = url,
            MessageBody = JsonSerializer.Serialize(envelope, JsonOptions),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [TypeAttribute] = new() { DataType = "String", StringValue = envelope.Type },
            },
        };

        var traceParent = envelope.TraceParent ?? Activity.Current?.Id;
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            request.MessageAttributes[TraceParentAttribute] = new() { DataType = "String", StringValue = traceParent };
        }

        await sqs.SendMessageAsync(request, cancellationToken);
        LogPublished(queue, envelope.Type, envelope.Id);
    }

    [LoggerMessage(EventId = 8010, Level = LogLevel.Debug, Message = "Published {Type} {MessageId} to {Queue}")]
    private partial void LogPublished(string queue, string type, string messageId);
}

/// <summary>Messaging disabled: callers keep the in-process path.</summary>
public sealed class NoOpMessagePublisher : IMessagePublisher
{
    public bool IsEnabled => false;

    public Task PublishAsync(string queue, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Messaging is disabled (Messaging:Sqs:Enabled = false); check IsEnabled before publishing.");
}
