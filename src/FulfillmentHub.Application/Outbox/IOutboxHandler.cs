using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Application.Outbox;

/// <summary>What the handler wants the publisher to do with the message afterwards.</summary>
public abstract record OutboxHandling
{
    /// <summary>The effect happened (or is no longer needed): mark the message processed.</summary>
    public sealed record Done : OutboxHandling;

    /// <summary>A transient problem: leave the message pending and try again later with backoff.</summary>
    public sealed record Retry(string Reason) : OutboxHandling;

    public static readonly OutboxHandling Completed = new Done();
}

/// <summary>
/// A consumer of one domain event type, resolved by the event's type name (ADR-004). Handlers must be idempotent:
/// the outbox is at-least-once, so the same message can be delivered twice (crash after the effect, before the mark).
/// </summary>
public interface IOutboxHandler
{
    Task<OutboxHandling> HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}

/// <summary>Typed base so handlers do not repeat the cast.</summary>
public abstract class OutboxHandler<TEvent> : IOutboxHandler
    where TEvent : class, IDomainEvent
{
    public Task<OutboxHandling> HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken) =>
        domainEvent is TEvent typed
            ? HandleAsync(typed, cancellationToken)
            : throw new InvalidOperationException($"{GetType().Name} cannot handle {domainEvent.GetType().Name}.");

    protected abstract Task<OutboxHandling> HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
