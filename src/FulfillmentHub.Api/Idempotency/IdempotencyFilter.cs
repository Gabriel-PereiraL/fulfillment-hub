using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Idempotency;

/// <summary>
/// Makes a mutating endpoint idempotent per client (ADR-010). The client sends an <c>Idempotency-Key</c>; the key,
/// scoped to the authenticated user, is claimed before the handler runs. A repeat with the same payload replays the
/// stored response; a repeat with a different payload is rejected (422); a concurrent repeat is told to wait (409).
/// The payload fingerprint is the canonical JSON of the bound <typeparamref name="TRequest"/> plus method and path.
/// Only responses below 500 are stored — after an unexpected failure the key is released so the client can retry.
/// </summary>
public sealed partial class IdempotencyFilter<TRequest, TResponse>(
    IdempotencyStore store,
    IdempotencyMetrics metrics,
    IOptions<JsonOptions> jsonOptions,
    ILogger<IdempotencyFilter<TRequest, TResponse>> logger) : IEndpointFilter
    where TRequest : class
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotent-Replayed";

    private const int MaxKeyLength = IdempotencyRecord.KeyMaxLength;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var key = httpContext.Request.Headers[HeaderName].FirstOrDefault();

        if (!IsValidKey(key))
        {
            return TypedResults.Problem(
                title: "Missing or invalid Idempotency-Key",
                detail: $"Provide an '{HeaderName}' header with 1 to {MaxKeyLength} characters (letters, digits, '-' or '_').",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["code"] = "idempotency.key_required" });
        }

        var currentUser = httpContext.RequestServices.GetRequiredService<ICurrentUser>();
        var scope = currentUser.UserId.Value.ToString("N");
        var request = context.Arguments.OfType<TRequest>().Single();
        var hash = Fingerprint(httpContext.Request.Method, httpContext.Request.Path, request);
        var cancellationToken = httpContext.RequestAborted;

        var outcome = await store.BeginAsync(scope, key, hash, cancellationToken);

        switch (outcome.Decision)
        {
            case IdempotencyDecision.Replay:
                metrics.Hit("replayed");
                LogReplayed(key);
                httpContext.Response.Headers[ReplayedHeaderName] = "true";
                if (outcome.Location is not null)
                {
                    httpContext.Response.Headers.Location = outcome.Location;
                }

                return outcome.Body is null
                    ? TypedResults.StatusCode(outcome.StatusCode!.Value)
                    : TypedResults.Content(outcome.Body, outcome.ContentType, Encoding.UTF8, outcome.StatusCode);

            case IdempotencyDecision.InProgress:
                metrics.Hit("conflict");
                return TypedResults.Problem(
                    title: "Request in progress",
                    detail: "A request with this Idempotency-Key is still being processed.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = "idempotency.in_progress" });

            case IdempotencyDecision.Mismatch:
                metrics.Hit("mismatch");
                return TypedResults.Problem(
                    title: "Idempotency-Key reused",
                    detail: "This Idempotency-Key was already used with a different request payload.",
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    extensions: new Dictionary<string, object?> { ["code"] = "idempotency.payload_mismatch" });
        }

        object? result;
        try
        {
            result = await next(context);
        }
        catch
        {
            await store.ReleaseAsync(scope, key, CancellationToken.None);
            throw;
        }

        var (statusCode, body, contentType, location) = Describe(result);

        if (statusCode is null || statusCode >= 500)
        {
            await store.ReleaseAsync(scope, key, CancellationToken.None);
            return result;
        }

        await store.CompleteAsync(scope, key, statusCode.Value, body, contentType, location, CancellationToken.None);
        return result;
    }

    private (int? StatusCode, string? Body, string? ContentType, string? Location) Describe(object? result)
    {
        while (result is INestedHttpResult nested)
        {
            result = nested.Result;
        }

        var options = jsonOptions.Value.SerializerOptions;

        return result switch
        {
            Created<TResponse> created => (created.StatusCode, JsonSerializer.Serialize(created.Value, options), "application/json", created.Location),
            ProblemHttpResult problem => (problem.StatusCode, JsonSerializer.Serialize(problem.ProblemDetails, options), problem.ContentType, null),
            IValueHttpResult { Value: not null } valued and IStatusCodeHttpResult { StatusCode: { } status } =>
                (status, JsonSerializer.Serialize(valued.Value, valued.Value.GetType(), options), "application/json", null),
            IStatusCodeHttpResult { StatusCode: { } status } => (status, null, null, null),
            _ => (null, null, null, null),
        };
    }

    private string Fingerprint(string method, PathString path, TRequest request)
    {
        var canonical = JsonSerializer.Serialize(request, jsonOptions.Value.SerializerOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{method} {path}\n{canonical}"));
        return Convert.ToHexStringLower(bytes);
    }

    private static bool IsValidKey([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length <= MaxKeyLength
        && key.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    [LoggerMessage(EventId = 4100, Level = LogLevel.Information, Message = "Replayed idempotent response for key {IdempotencyKey}")]
    private partial void LogReplayed(string idempotencyKey);
}
