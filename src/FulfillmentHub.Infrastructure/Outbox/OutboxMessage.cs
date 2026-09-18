namespace FulfillmentHub.Infrastructure.Outbox;

public enum OutboxMessageStatus
{
    Pending = 0,
    Processed = 1,
    Failed = 2,
}

/// <summary>
/// A domain event persisted in the same transaction as the aggregate change that raised it (ADR-004). The Worker
/// publishes it at least once; <see cref="Attempts"/>/<see cref="NextAttemptAt"/> drive the backoff and
/// <see cref="OutboxMessageStatus.Failed"/> is the logical dead letter, visible and retryable by an administrator.
/// </summary>
public sealed class OutboxMessage
{
    public const int TypeMaxLength = 100;
    public const int LastErrorMaxLength = 1000;

    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Domain event type name (e.g. <c>OrderPlaced</c>); the handler key.</summary>
    public string Type { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    /// <summary>The aggregate the event belongs to, for tracing and ordering per aggregate.</summary>
    public Guid AggregateId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public OutboxMessageStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    /// <summary>A publisher that claimed the message owns it until this instant; a crashed publisher's lease simply expires.</summary>
    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? LastError { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? TraceParent { get; private set; }

    public static OutboxMessage Create(string type, string payload, Guid aggregateId, DateTimeOffset occurredAt, DateTimeOffset now, string? correlationId, string? traceParent) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = type,
        Payload = payload,
        AggregateId = aggregateId,
        OccurredAt = occurredAt,
        CreatedAt = now,
        Status = OutboxMessageStatus.Pending,
        Attempts = 0,
        NextAttemptAt = now,
        CorrelationId = correlationId,
        TraceParent = traceParent,
    };

    public void Lease(DateTimeOffset until) => LockedUntil = until;

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = OutboxMessageStatus.Processed;
        ProcessedAt = now;
        LockedUntil = null;
        LastError = null;
    }

    /// <summary>Records a failed attempt; after <paramref name="maxAttempts"/> the message is parked as Failed.</summary>
    public void MarkAttemptFailed(string error, TimeSpan backoff, int maxAttempts, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > LastErrorMaxLength ? error[..LastErrorMaxLength] : error;
        LockedUntil = null;

        if (Attempts >= maxAttempts)
        {
            Status = OutboxMessageStatus.Failed;
            return;
        }

        NextAttemptAt = now + backoff;
    }

    /// <summary>Administrative reprocessing: back to the queue with a clean attempt counter.</summary>
    public void Requeue(DateTimeOffset now)
    {
        Status = OutboxMessageStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
        LockedUntil = null;
        ProcessedAt = null;
    }
}
