using System.Diagnostics;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Application.Outbox;
using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Outbox;

public sealed record OutboxBatchSummary(int Claimed, int Processed, int Retried, int Failed);

/// <summary>
/// One publisher pass (ADR-004): claim a batch of due messages with <c>FOR UPDATE SKIP LOCKED</c> and a lease (several
/// publishers can run side by side), dispatch each one to its handler in a fresh DI scope, then mark it processed or
/// schedule the retry (exponential backoff + jitter; <c>Failed</c> after the attempt budget). Handler exceptions and
/// failure results are treated alike: the message stays and is retried.
/// </summary>
public sealed partial class OutboxProcessor(
    FulfillmentHubDbContext db,
    OutboxDispatcher dispatcher,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    OutboxMetrics metrics,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger)
{
    public async Task<OutboxBatchSummary> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var claimed = await ClaimAsync(settings, cancellationToken);
        var processed = 0;
        var retried = 0;
        var failed = 0;

        foreach (var message in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outcome = await DispatchAsync(message, cancellationToken);
            var now = timeProvider.GetUtcNow();

            switch (outcome)
            {
                case OutboxHandling.Done:
                    message.MarkProcessed(now);
                    processed++;
                    metrics.Published("processed", message.Type);
                    metrics.Lag(now - message.OccurredAt, message.Type);
                    break;

                case OutboxHandling.Retry retry:
                    message.MarkAttemptFailed(retry.Reason, Backoff(settings, message.Attempts + 1), settings.MaxAttempts, now);
                    if (message.Status == OutboxMessageStatus.Failed)
                    {
                        failed++;
                        metrics.Published("failed", message.Type);
                        LogFailed(message.Id, message.Type, message.Attempts, retry.Reason);
                    }
                    else
                    {
                        retried++;
                        metrics.Published("retried", message.Type);
                        LogRetry(message.Id, message.Type, message.Attempts, message.NextAttemptAt, retry.Reason);
                    }

                    break;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await RefreshBacklogAsync(cancellationToken);
        return new OutboxBatchSummary(claimed.Count, processed, retried, failed);
    }

    /// <summary>Marks due messages as leased in one short transaction; concurrent publishers skip each other's rows.</summary>
    private async Task<List<OutboxMessage>> ClaimAsync(OutboxOptions settings, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var lockedUntil = now + settings.Lease;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var messages = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT *, xmin FROM outbox_messages
                WHERE status = 'Pending' AND next_attempt_at <= {now} AND (locked_until IS NULL OR locked_until < {now})
                ORDER BY occurred_at
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Lease(lockedUntil);
        }

        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    /// <summary>
    /// With messaging on, "publishing" means handing the envelope to the queue (the consumer runs the handler with its own
    /// deduplication); with messaging off the handler runs right here (Phase 8 mode, D-70).
    /// </summary>
    private async Task<OutboxHandling> DispatchAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!publisher.IsEnabled)
            {
                return await dispatcher.DispatchAsync(message.Type, message.Payload, message.TraceParent, dedup: null, cancellationToken);
            }

            var envelope = new MessageEnvelope(message.Id.ToString(), message.Type, message.Payload, message.OccurredAt, message.CorrelationId, message.TraceParent);
            await publisher.PublishAsync(Queues.DomainEvents, envelope, cancellationToken);
            return OutboxHandling.Completed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPublishFailed(exception, message.Id, message.Type);
            return new OutboxHandling.Retry(exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            metrics.Duration(stopwatch.Elapsed, message.Type);
        }
    }

    /// <summary>Exponential backoff with full jitter, capped: 2 s, 4 s, 8 s … up to <see cref="OutboxOptions.MaxDelay"/>.</summary>
    internal static TimeSpan Backoff(OutboxOptions settings, int attempt)
    {
        var exponential = settings.BaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var capped = Math.Min(exponential, settings.MaxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(capped * (0.5 + (Random.Shared.NextDouble() / 2)));
    }

    private async Task RefreshBacklogAsync(CancellationToken cancellationToken)
    {
        var pending = await db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Pending, cancellationToken);
        var failed = await db.OutboxMessages.CountAsync(m => m.Status == OutboxMessageStatus.Failed, cancellationToken);
        metrics.Backlog(pending, failed);
    }

    [LoggerMessage(EventId = 7000, Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) attempt {Attempt} failed, next attempt at {NextAttemptAt}: {Reason}")]
    private partial void LogRetry(Guid messageId, string type, int attempt, DateTimeOffset nextAttemptAt, string reason);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Error, Message = "Outbox message {MessageId} ({Type}) parked as Failed after {Attempts} attempts: {Reason}")]
    private partial void LogFailed(Guid messageId, string type, int attempts, string reason);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Error, Message = "Publishing outbox message {MessageId} ({Type}) to the queue failed")]
    private partial void LogPublishFailed(Exception exception, Guid messageId, string type);
}
