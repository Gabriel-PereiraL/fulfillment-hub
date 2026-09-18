using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Messaging;

/// <summary>
/// SQS settings (section <c>Messaging:Sqs</c>). Locally the endpoint is LocalStack with its well-known test
/// credentials; on AWS <see cref="ServiceUrl"/> stays empty and the SDK's default credential chain applies.
/// </summary>
public sealed class SqsOptions
{
    public const string SectionName = "Messaging:Sqs";

    /// <summary>Off = outbox handlers run in-process and webhooks are processed in the request (Phase 8 behaviour).</summary>
    public bool Enabled { get; init; }

    /// <summary>LocalStack endpoint (e.g. http://localhost:4566); empty for real AWS.</summary>
    public string? ServiceUrl { get; init; }

    [Required]
    public string Region { get; init; } = "us-east-1";

    /// <summary>Static credentials; only for LocalStack ("test"/"test"). Empty on AWS (roles/env/profile chain).</summary>
    public string? AccessKey { get; init; }

    public string? SecretKey { get; init; }

    /// <summary>Optional prefix so several environments can share one account (e.g. <c>dev-</c>).</summary>
    public string QueuePrefix { get; init; } = string.Empty;

    public string DeadLetterSuffix { get; init; } = "-dlq";

    /// <summary>Receives before a message is moved to the dead-letter queue (redrive policy).</summary>
    [Range(1, 1000)]
    public int MaxReceiveCount { get; init; } = 5;

    /// <summary>Must exceed the slowest handler; a message becomes visible again after this if not deleted.</summary>
    [Range(1, 43_200)]
    public int VisibilityTimeoutSeconds { get; init; } = 60;

    /// <summary>Long polling wait (0–20).</summary>
    [Range(0, 20)]
    public int WaitTimeSeconds { get; init; } = 20;

    [Range(1, 10)]
    public int BatchSize { get; init; } = 10;

    /// <summary>Messages handled at the same time per consumer.</summary>
    [Range(1, 64)]
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>Backoff applied through ChangeMessageVisibility after a failed handling: base × 2^(receives-1), capped.</summary>
    [Range(1, 900)]
    public int RetryBaseDelaySeconds { get; init; } = 5;

    [Range(1, 43_200)]
    public int RetryMaxDelaySeconds { get; init; } = 300;

    public string QueueName(string logicalName) => QueuePrefix + logicalName;

    public string DeadLetterQueueName(string logicalName) => QueueName(logicalName) + DeadLetterSuffix;
}
