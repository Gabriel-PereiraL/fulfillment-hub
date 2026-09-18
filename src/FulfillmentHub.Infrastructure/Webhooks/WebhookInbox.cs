using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FulfillmentHub.Infrastructure.Webhooks;

/// <summary>
/// Records incoming webhooks in their own unit of work before any processing, so that a duplicate delivery is
/// detected by the database even when two copies arrive at the same time (unique index on provider + event id).
/// </summary>
public sealed class WebhookInbox(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    /// <summary>Returns the new record, or null when this provider event was already received.</summary>
    public async Task<WebhookEvent?> TryRecordAsync(
        string provider,
        string providerEventId,
        string eventType,
        string payload,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

        // Cheap check first so the common retry/duplicate case does not surface as a database error;
        // the unique index remains the actual guarantee for two copies arriving at the same instant.
        if (await db.WebhookEvents.AnyAsync(e => e.Provider == provider && e.ProviderEventId == providerEventId, cancellationToken))
        {
            return null;
        }

        var webhookEvent = WebhookEvent.Receive(provider, providerEventId, eventType, payload, correlationId, timeProvider.GetUtcNow());
        db.Add(webhookEvent);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return webhookEvent;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return null;
        }
    }

    public Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken) =>
        UpdateAsync(id, e => e.MarkProcessed(timeProvider.GetUtcNow()), cancellationToken);

    public Task MarkIgnoredAsync(Guid id, string reason, CancellationToken cancellationToken) =>
        UpdateAsync(id, e => e.MarkIgnored(reason, timeProvider.GetUtcNow()), cancellationToken);

    public Task MarkFailedAsync(Guid id, string error, CancellationToken cancellationToken) =>
        UpdateAsync(id, e => e.MarkFailed(error, timeProvider.GetUtcNow()), cancellationToken);

    private async Task UpdateAsync(Guid id, Action<WebhookEvent> update, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var webhookEvent = await db.WebhookEvents.SingleAsync(e => e.Id == id, cancellationToken);
        update(webhookEvent);
        await db.SaveChangesAsync(cancellationToken);
    }
}
