using System.Text;
using System.Text.Json;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Webhooks;

/// <summary>How a provider signs its webhooks and which key/window to verify them with.</summary>
public sealed record WebhookSource(string Provider, string SignatureHeader, string TimestampHeader, string SigningKey, TimeSpan TimestampTolerance);

/// <summary>What the endpoint-specific code found in a verified, deduplicated payload.</summary>
public sealed record WebhookIdentity(string EventId, string EventType);

public abstract record WebhookOutcome
{
    public sealed record Processed : WebhookOutcome;

    public sealed record Ignored(string Reason) : WebhookOutcome;

    public sealed record Failed(string Error) : WebhookOutcome;
}

/// <summary>
/// The inbound webhook pipeline shared by every provider (docs/INTEGRATIONS.md §5): size limit → raw-body HMAC and
/// timestamp check → parse → persist in the inbox (dedup by provider event id) → process → always 200 once stored.
/// Anonymous by design: the signature authenticates the caller.
/// </summary>
public sealed partial class WebhookReceiver(
    WebhookSignatureVerifier verifier,
    WebhookInbox inbox,
    WebhooksMetrics metrics,
    ILogger<WebhookReceiver> logger)
{
    public const string RateLimitPolicy = "webhooks";
    public const int MaxBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<Results<Ok, ProblemHttpResult, StatusCodeHttpResult>> ReceiveAsync<TPayload>(
        HttpContext httpContext,
        WebhookSource source,
        Func<TPayload, WebhookIdentity?> identify,
        Func<TPayload, CancellationToken, Task<WebhookOutcome>> process,
        CancellationToken cancellationToken)
        where TPayload : class
    {
        var request = httpContext.Request;

        if (request.ContentLength > MaxBodyBytes)
        {
            metrics.Rejected(source.Provider, "too_large");
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            metrics.Rejected(source.Provider, "too_large");
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var signature = verifier.Verify(
            source.SigningKey,
            body,
            request.Headers[source.SignatureHeader].FirstOrDefault(),
            request.Headers[source.TimestampHeader].FirstOrDefault(),
            source.TimestampTolerance);

        if (signature != WebhookSignatureResult.Valid)
        {
            metrics.Rejected(source.Provider, signature.ToString());
            LogRejected(source.Provider, signature);
            return TypedResults.Problem(title: "Webhook rejected", detail: "Invalid or missing signature.", statusCode: StatusCodes.Status401Unauthorized);
        }

        TPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        var identity = payload is null ? null : identify(payload);
        if (payload is null || identity is null)
        {
            metrics.Rejected(source.Provider, "malformed");
            return TypedResults.Problem(title: "Webhook rejected", detail: "Malformed event payload.", statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault();
        var recorded = await inbox.TryRecordAsync(source.Provider, identity.EventId, identity.EventType, Encoding.UTF8.GetString(body), correlationId, cancellationToken);

        if (recorded is null)
        {
            metrics.Duplicate(source.Provider);
            LogDuplicate(source.Provider, identity.EventId);
            return TypedResults.Ok();
        }

        metrics.Received(source.Provider, identity.EventType);

        // Processing failures are recorded, not surfaced: the event is safely stored and reconciliation catches up.
        try
        {
            switch (await process(payload, cancellationToken))
            {
                case WebhookOutcome.Processed:
                    await inbox.MarkProcessedAsync(recorded.Id, cancellationToken);
                    break;
                case WebhookOutcome.Ignored ignored:
                    await inbox.MarkIgnoredAsync(recorded.Id, ignored.Reason, cancellationToken);
                    break;
                case WebhookOutcome.Failed failed:
                    await inbox.MarkFailedAsync(recorded.Id, failed.Error, cancellationToken);
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProcessingFailed(exception, source.Provider, identity.EventId);
            await inbox.MarkFailedAsync(recorded.Id, exception.GetType().Name + ": " + exception.Message, CancellationToken.None);
        }

        return TypedResults.Ok();
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8 * 1024];
        int read;

        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);

            if (buffer.Length > MaxBodyBytes)
            {
                return null;
            }
        }

        return buffer.ToArray();
    }

    [LoggerMessage(EventId = 5200, Level = LogLevel.Warning, Message = "Webhook from {Provider} rejected: {Reason}")]
    private partial void LogRejected(string provider, WebhookSignatureResult reason);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information, Message = "Webhook {Provider}/{ProviderEventId} already received; ignoring duplicate")]
    private partial void LogDuplicate(string provider, string providerEventId);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "Processing webhook {Provider}/{ProviderEventId} failed")]
    private partial void LogProcessingFailed(Exception exception, string provider, string providerEventId);
}
