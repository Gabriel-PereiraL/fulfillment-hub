using System.Diagnostics;
using FulfillmentHub.Application.Outbox;
using FulfillmentHub.Infrastructure.Messaging;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Infrastructure.Outbox;

/// <summary>
/// Runs the handler of one domain event in a fresh DI scope. Used by the outbox publisher (in-process mode) and by the
/// SQS consumer; the latter passes a deduplication key so the effect and the <c>processed_messages</c> row commit in
/// the same transaction — a redelivery of the same message is then acknowledged without a second effect.
/// </summary>
public sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    OutboxEventSerializer serializer,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger)
{
    private static readonly ActivitySource ActivitySource = new(TelemetryNames.ActivitySource);

    public async Task<OutboxHandling> DispatchAsync(
        string type,
        string payload,
        string? traceParent,
        (string Consumer, string MessageId)? dedup,
        CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity($"Outbox {type}", ActivityKind.Consumer, traceParent);
        activity?.SetTag("messaging.message.id", dedup?.MessageId);

        try
        {
            var domainEvent = serializer.Deserialize(type, payload);
            if (domainEvent is null)
            {
                return new OutboxHandling.Retry($"unknown event type '{type}'");
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetKeyedService<IOutboxHandler>(type);

            if (handler is null)
            {
                // No consumer for this event (yet): nothing to do, and no point retrying.
                return OutboxHandling.Completed;
            }

            if (dedup is null)
            {
                return await handler.HandleAsync(domainEvent, cancellationToken);
            }

            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            var (consumer, messageId) = dedup.Value;

            if (await db.ProcessedMessages.AnyAsync(m => m.Consumer == consumer && m.MessageId == messageId, cancellationToken))
            {
                LogDuplicate(consumer, messageId, type);
                return OutboxHandling.Completed;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.ProcessedMessages.Add(ProcessedMessage.Create(consumer, messageId, timeProvider.GetUtcNow()));
            await db.SaveChangesAsync(cancellationToken);

            var handling = await handler.HandleAsync(domainEvent, cancellationToken);

            if (handling is OutboxHandling.Done)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await transaction.RollbackAsync(cancellationToken); // effect and dedup row disappear together; redelivery retries
            }

            return handling;
        }
        catch (DbUpdateException exception) when (dedup is not null && exception.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
        {
            // Two consumers raced on the same message: the other one won the dedup row.
            LogDuplicate(dedup.Value.Consumer, dedup.Value.MessageId, type);
            return OutboxHandling.Completed;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            LogHandlerThrew(exception, type, dedup?.MessageId);
            return new OutboxHandling.Retry(exception.GetType().Name + ": " + exception.Message);
        }
    }

    [LoggerMessage(EventId = 7010, Level = LogLevel.Information, Message = "Consumer {Consumer} already processed message {MessageId} ({Type}); acknowledging without effect")]
    private partial void LogDuplicate(string consumer, string messageId, string type);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Error, Message = "Handler for {Type} (message {MessageId}) threw")]
    private partial void LogHandlerThrew(Exception exception, string type, string? messageId);
}
