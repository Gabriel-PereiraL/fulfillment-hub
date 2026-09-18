using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker.Messaging;

/// <summary>
/// Base SQS consumer (ADR-005): long polling, bounded concurrency, delete only after the handler succeeded, and on
/// failure a shorter visibility timeout as backoff so the message comes back sooner than the queue default — until
/// the redrive policy moves it to the dead-letter queue after <c>MaxReceiveCount</c> receives. A poll error is logged
/// and the loop waits a little; the consumer never stops on its own.
/// </summary>
public abstract partial class SqsConsumer(
    IAmazonSQS sqs,
    SqsQueueProvisioner queues,
    IOptions<SqsOptions> options,
    QueueMetrics metrics,
    TimeProvider timeProvider,
    ILogger logger) : BackgroundService
{
    private static readonly ActivitySource ActivitySource = new(FulfillmentHub.Infrastructure.Telemetry.TelemetryNames.ActivitySource);
    private static readonly TimeSpan PollErrorDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DlqDepthInterval = TimeSpan.FromSeconds(30);

    protected abstract string ConsumerName { get; }

    protected abstract string LogicalQueue { get; }

    /// <summary>True = done (delete); false = leave it for redelivery.</summary>
    protected abstract Task<bool> HandleAsync(MessageEnvelope envelope, int receiveCount, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            LogDisabled(ConsumerName);
            return;
        }

        string queueUrl;
        try
        {
            queueUrl = await queues.GetQueueUrlAsync(LogicalQueue, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var queueName = settings.QueueName(LogicalQueue);
        LogStarted(ConsumerName, queueName, settings.MaxConcurrency);
        using var concurrency = new SemaphoreSlim(settings.MaxConcurrency);
        var lastDepthRefresh = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = settings.BatchSize,
                    WaitTimeSeconds = settings.WaitTimeSeconds,
                    MessageSystemAttributeNames = ["ApproximateReceiveCount", "SentTimestamp"],
                    MessageAttributeNames = ["All"],
                }, stoppingToken);

                var messages = response.Messages ?? [];
                var tasks = new List<Task>(messages.Count);
                foreach (var message in messages)
                {
                    await concurrency.WaitAsync(stoppingToken);
                    tasks.Add(ProcessAsync(queueUrl, message, settings, concurrency, stoppingToken));
                }

                await Task.WhenAll(tasks);

                if (timeProvider.GetUtcNow() - lastDepthRefresh > DlqDepthInterval)
                {
                    lastDepthRefresh = timeProvider.GetUtcNow();
                    await RefreshDeadLetterDepthAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPollFailed(exception, ConsumerName);
                await Task.Delay(PollErrorDelay, timeProvider, stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(string queueUrl, Message message, SqsOptions settings, SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        try
        {
            var receiveCount = int.TryParse(message.Attributes?.GetValueOrDefault("ApproximateReceiveCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 1;
            var traceParent = message.MessageAttributes?.GetValueOrDefault(SqsMessagePublisher.TraceParentAttribute)?.StringValue;

            using var activity = ActivitySource.StartActivity($"{settings.QueueName(LogicalQueue)} receive", ActivityKind.Consumer, traceParent);
            activity?.SetTag("messaging.system", "aws_sqs");
            activity?.SetTag("messaging.destination.name", settings.QueueName(LogicalQueue));
            activity?.SetTag("messaging.message.id", message.MessageId);
            activity?.SetTag("messaging.receive_count", receiveCount);

            RecordAge(message);

            MessageEnvelope? envelope;
            try
            {
                envelope = JsonSerializer.Deserialize<MessageEnvelope>(message.Body, SqsMessagePublisher.JsonOptions);
            }
            catch (JsonException)
            {
                envelope = null;
            }

            var handled = envelope is not null && await HandleAsync(envelope, receiveCount, cancellationToken);

            if (handled)
            {
                await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
                metrics.Processed(LogicalQueue, ConsumerName);
                return;
            }

            metrics.Failed(LogicalQueue, ConsumerName, envelope is null ? "malformed" : "handler");
            activity?.SetStatus(ActivityStatusCode.Error, "not handled");

            var backoff = Backoff(settings, receiveCount);
            LogNotHandled(ConsumerName, message.MessageId, receiveCount, settings.MaxReceiveCount, backoff);
            await sqs.ChangeMessageVisibilityAsync(queueUrl, message.ReceiptHandle, (int)backoff.TotalSeconds, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProcessingFailed(exception, ConsumerName, message.MessageId);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private void RecordAge(Message message)
    {
        if (long.TryParse(message.Attributes?.GetValueOrDefault("SentTimestamp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sentMs))
        {
            metrics.Age(LogicalQueue, timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(sentMs));
        }
    }

    private async Task RefreshDeadLetterDepthAsync(CancellationToken cancellationToken)
    {
        try
        {
            var dlqUrl = await queues.GetDeadLetterQueueUrlAsync(LogicalQueue, cancellationToken);
            var attributes = await sqs.GetQueueAttributesAsync(dlqUrl, [QueueAttributeName.ApproximateNumberOfMessages], cancellationToken);
            metrics.DlqDepth(LogicalQueue, attributes.ApproximateNumberOfMessages);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPollFailed(exception, ConsumerName);
        }
    }

    /// <summary>Full-jitter exponential backoff on the visibility timeout: base × 2^(receives-1), capped.</summary>
    internal static TimeSpan Backoff(SqsOptions settings, int receiveCount)
    {
        var exponential = settings.RetryBaseDelaySeconds * Math.Pow(2, Math.Max(0, receiveCount - 1));
        var capped = Math.Min(exponential, settings.RetryMaxDelaySeconds);
        return TimeSpan.FromSeconds(Math.Max(1, capped * (0.5 + (Random.Shared.NextDouble() / 2))));
    }

    [LoggerMessage(EventId = 8100, Level = LogLevel.Information, Message = "{Consumer} consuming {Queue} (concurrency {Concurrency})")]
    private partial void LogStarted(string consumer, string queue, int concurrency);

    [LoggerMessage(EventId = 8101, Level = LogLevel.Information, Message = "{Consumer} not started: messaging is disabled")]
    private partial void LogDisabled(string consumer);

    [LoggerMessage(EventId = 8102, Level = LogLevel.Warning, Message = "{Consumer} left message {MessageId} for redelivery (receive {ReceiveCount}/{MaxReceiveCount}, visible again in {Backoff})")]
    private partial void LogNotHandled(string consumer, string messageId, int receiveCount, int maxReceiveCount, TimeSpan backoff);

    [LoggerMessage(EventId = 8103, Level = LogLevel.Error, Message = "{Consumer} failed while handling message {MessageId}")]
    private partial void LogProcessingFailed(Exception exception, string consumer, string messageId);

    [LoggerMessage(EventId = 8104, Level = LogLevel.Error, Message = "{Consumer} poll failed; retrying shortly")]
    private partial void LogPollFailed(Exception exception, string consumer);
}
