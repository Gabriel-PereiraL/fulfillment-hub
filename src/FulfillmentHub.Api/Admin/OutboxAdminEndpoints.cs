using FulfillmentHub.Api.Identity;
using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Api.Admin;

/// <summary>Operational view of the outbox (ADR-004): the logical dead letter is visible and retryable by administrators.</summary>
public static partial class OutboxAdminEndpoints
{
    public static IEndpointRouteBuilder MapOutboxAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/outbox")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly)
            .WithTags("Admin");

        group.MapGet("/", ListAsync)
            .WithName("ListOutboxMessages")
            .WithSummary("Lists outbox messages by status (default: Failed), newest first.")
            .Produces<IReadOnlyList<OutboxMessageDto>>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/retry", RetryAsync)
            .WithName("RetryOutboxMessage")
            .WithSummary("Puts a Failed (or already Processed) message back in the queue with a clean attempt counter.")
            .Produces<OutboxMessageDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<IReadOnlyList<OutboxMessageDto>>, ProblemHttpResult>> ListAsync(
        string? status,
        int? limit,
        FulfillmentHubDbContext db,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<OutboxMessageStatus>(status ?? nameof(OutboxMessageStatus.Failed), ignoreCase: true, out var parsed))
        {
            return TypedResults.Problem(title: "Invalid status", detail: "Use Pending, Processed or Failed.", statusCode: StatusCodes.Status400BadRequest);
        }

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var messages = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Status == parsed)
            .OrderByDescending(m => m.OccurredAt)
            .Take(take)
            .Select(m => OutboxMessageDto.From(m))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<OutboxMessageDto>>(messages);
    }

    private static async Task<Results<Ok<OutboxMessageDto>, NotFound>> RetryAsync(
        Guid id,
        FulfillmentHubDbContext db,
        TimeProvider timeProvider,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var message = await db.OutboxMessages.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null)
        {
            return TypedResults.NotFound();
        }

        message.Requeue(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        LogRequeued(logger, message.Id, message.Type);

        return TypedResults.Ok(OutboxMessageDto.From(message));
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Warning, Message = "Outbox message {MessageId} ({Type}) requeued by an administrator")]
    private static partial void LogRequeued(ILogger logger, Guid messageId, string type);
}

public sealed record OutboxMessageDto(
    Guid Id,
    string Type,
    Guid AggregateId,
    string Status,
    int Attempts,
    DateTimeOffset OccurredAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? ProcessedAt,
    string? LastError,
    string? CorrelationId)
{
    public static OutboxMessageDto From(OutboxMessage m) =>
        new(m.Id, m.Type, m.AggregateId, m.Status.ToString(), m.Attempts, m.OccurredAt, m.NextAttemptAt, m.ProcessedAt, m.LastError, m.CorrelationId);
}
