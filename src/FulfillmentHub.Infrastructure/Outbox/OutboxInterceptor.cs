using System.Diagnostics;
using FulfillmentHub.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FulfillmentHub.Infrastructure.Outbox;

/// <summary>
/// Turns the domain events raised by tracked aggregates into <see cref="OutboxMessage"/> rows inside the same
/// <c>SaveChanges</c> (ADR-004): either both the state change and its events are committed, or neither. Events are
/// cleared only after the save succeeded, so a failed commit leaves them to be retried with the aggregate.
/// </summary>
public sealed class OutboxInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enqueue(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ClearEvents(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        ClearEvents(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAggregateRoot { DomainEvents.Count: > 0 })
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var now = timeProvider.GetUtcNow();
        var correlationId = Activity.Current?.GetTagItem("correlation.id") as string;
        var traceParent = Activity.Current?.Id;

        foreach (var entry in aggregates)
        {
            var aggregate = (IAggregateRoot)entry.Entity;
            var aggregateId = entry.Property("Id").CurrentValue is { } id ? ToGuid(id) : Guid.Empty;

            foreach (var domainEvent in aggregate.DomainEvents)
            {
                context.Add(OutboxMessage.Create(
                    OutboxEventSerializer.TypeNameOf(domainEvent),
                    OutboxEventSerializer.Serialize(domainEvent),
                    aggregateId,
                    domainEvent.OccurredAt,
                    now,
                    correlationId,
                    traceParent));
            }
        }
    }

    private static void ClearEvents(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is IAggregateRoot aggregate)
            {
                aggregate.ClearDomainEvents();
            }
        }
    }

    private static Guid ToGuid(object id) => id switch
    {
        Guid guid => guid,
        _ => id.GetType().GetProperty("Value")?.GetValue(id) is Guid inner ? inner : Guid.Empty,
    };
}
