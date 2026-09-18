namespace FulfillmentHub.Application.Messaging;

/// <summary>Logical queue names (ADR-005). The provider (SQS) may map them to URLs/ARNs.</summary>
public static class Queues
{
    public const string DomainEvents = "fh-domain-events";
    public const string WebhooksInbound = "fh-webhooks-inbound";
}

/// <summary>
/// What travels on a queue: an id for deduplication, a type for dispatch, an opaque JSON payload and the tracing
/// context of the request that produced it. Messages are contracts: keep them flat and primitive.
/// </summary>
public sealed record MessageEnvelope(
    string Id,
    string Type,
    string Payload,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    string? TraceParent);

/// <summary>
/// Port to the message broker. When messaging is disabled (<see cref="IsEnabled"/> false) callers keep the
/// in-process path (Phase 8 behaviour), so the system runs without a queue in tests and small setups.
/// </summary>
public interface IMessagePublisher
{
    bool IsEnabled { get; }

    /// <summary>Publishes and returns when the broker acknowledged the message. Throws on broker failure.</summary>
    Task PublishAsync(string queue, MessageEnvelope envelope, CancellationToken cancellationToken);
}
