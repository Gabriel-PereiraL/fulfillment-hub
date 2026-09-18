using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using FulfillmentHub.Application.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Messaging;

/// <summary>
/// Creates the queues of ADR-005 (each with its dead-letter queue and redrive policy) idempotently, and resolves
/// logical names to URLs. On AWS the queues would normally come from IaC (Phase 15); creating them here keeps
/// LocalStack and tests self-contained.
/// </summary>
public sealed partial class SqsQueueProvisioner(IAmazonSQS sqs, IOptions<SqsOptions> options, ILogger<SqsQueueProvisioner> logger)
{
    public static readonly string[] LogicalQueues = [Queues.DomainEvents, Queues.WebhooksInbound];

    private readonly ConcurrentDictionary<string, string> _urls = new(StringComparer.Ordinal);

    public async Task EnsureQueuesAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        foreach (var logical in LogicalQueues)
        {
            var dlqName = settings.DeadLetterQueueName(logical);
            var dlqUrl = (await sqs.CreateQueueAsync(new CreateQueueRequest { QueueName = dlqName }, cancellationToken)).QueueUrl;
            var dlqArn = (await sqs.GetQueueAttributesAsync(dlqUrl, [QueueAttributeName.QueueArn], cancellationToken)).QueueARN;

            var redrive = JsonSerializer.Serialize(new { deadLetterTargetArn = dlqArn, maxReceiveCount = settings.MaxReceiveCount });
            var request = new CreateQueueRequest
            {
                QueueName = settings.QueueName(logical),
                Attributes = new Dictionary<string, string>
                {
                    [QueueAttributeName.VisibilityTimeout] = settings.VisibilityTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [QueueAttributeName.RedrivePolicy] = redrive,
                    [QueueAttributeName.ReceiveMessageWaitTimeSeconds] = settings.WaitTimeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
            };

            var queueName = settings.QueueName(logical);
            var url = (await sqs.CreateQueueAsync(request, cancellationToken)).QueueUrl;
            _urls[queueName] = url;
            _urls[dlqName] = dlqUrl;
            LogEnsured(queueName, dlqName, settings.MaxReceiveCount);
        }
    }

    /// <summary>URL of a logical queue, resolved from the broker when not created by this process.</summary>
    public async Task<string> GetQueueUrlAsync(string logicalName, CancellationToken cancellationToken)
    {
        var name = options.Value.QueueName(logicalName);
        if (_urls.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var url = (await sqs.GetQueueUrlAsync(name, cancellationToken)).QueueUrl;
        _urls[name] = url;
        return url;
    }

    public Task<string> GetDeadLetterQueueUrlAsync(string logicalName, CancellationToken cancellationToken) =>
        GetQueueUrlByNameAsync(options.Value.DeadLetterQueueName(logicalName), cancellationToken);

    private async Task<string> GetQueueUrlByNameAsync(string name, CancellationToken cancellationToken)
    {
        if (_urls.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var url = (await sqs.GetQueueUrlAsync(name, cancellationToken)).QueueUrl;
        _urls[name] = url;
        return url;
    }

    [LoggerMessage(EventId = 8000, Level = LogLevel.Information, Message = "Queue {Queue} ready (dead-letter {DeadLetterQueue} after {MaxReceiveCount} receives)")]
    private partial void LogEnsured(string queue, string deadLetterQueue, int maxReceiveCount);
}
