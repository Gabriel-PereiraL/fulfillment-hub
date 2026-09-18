using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FulfillmentHub.Infrastructure.Idempotency;

public enum IdempotencyDecision
{
    /// <summary>No prior request: the caller owns the key and must execute the operation.</summary>
    Proceed = 0,

    /// <summary>The same request already completed: replay the stored response.</summary>
    Replay = 1,

    /// <summary>The same request is being processed right now by another caller.</summary>
    InProgress = 2,

    /// <summary>The key was reused with a different payload.</summary>
    Mismatch = 3,
}

public sealed record IdempotencyOutcome(
    IdempotencyDecision Decision,
    int? StatusCode = null,
    string? Body = null,
    string? ContentType = null,
    string? Location = null);

/// <summary>
/// Persists idempotency records in their own unit of work, independent of the request's <c>DbContext</c>: the key
/// must be claimed before the handler runs and finalized after it, whatever happened to the handler's transaction.
/// Concurrency between callers using the same key is resolved by the primary key (exactly one insert succeeds).
/// </summary>
public sealed class IdempotencyStore(IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromHours(24);

    public async Task<IdempotencyOutcome> BeginAsync(string scope, string key, string requestHash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await using (var insertScope = scopeFactory.CreateAsyncScope())
        {
            var db = insertScope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            db.IdempotencyRecords.Add(IdempotencyRecord.Start(scope, key, requestHash, now, TimeToLive));

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return new IdempotencyOutcome(IdempotencyDecision.Proceed);
            }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // Someone (maybe this same client retrying) already claimed the key: inspect it below.
            }
        }

        await using var scope2 = scopeFactory.CreateAsyncScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var existing = await db2.IdempotencyRecords.SingleAsync(r => r.Scope == scope && r.Key == key, cancellationToken);

        if (existing.IsExpired(now))
        {
            existing.Restart(requestHash, now, TimeToLive);
            await db2.SaveChangesAsync(cancellationToken);
            return new IdempotencyOutcome(IdempotencyDecision.Proceed);
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new IdempotencyOutcome(IdempotencyDecision.Mismatch);
        }

        return existing.Status == IdempotencyStatus.Completed
            ? new IdempotencyOutcome(IdempotencyDecision.Replay, existing.ResponseStatusCode, existing.ResponseBody, existing.ResponseContentType, existing.ResponseLocation)
            : new IdempotencyOutcome(IdempotencyDecision.InProgress);
    }

    public async Task CompleteAsync(string scope, string key, int statusCode, string? body, string? contentType, string? location, CancellationToken cancellationToken)
    {
        await using var serviceScope = scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var record = await db.IdempotencyRecords.SingleAsync(r => r.Scope == scope && r.Key == key, cancellationToken);

        record.Complete(statusCode, body, contentType, location);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Frees the key after an unexpected failure so the client can retry the same request.</summary>
    public async Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken)
    {
        await using var serviceScope = scopeFactory.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

        await db.IdempotencyRecords
            .Where(r => r.Scope == scope && r.Key == key && r.Status == IdempotencyStatus.InProgress)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
