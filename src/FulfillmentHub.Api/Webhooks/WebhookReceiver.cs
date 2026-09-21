using System.Text;
using System.Text.Json;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Messaging;
using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Webhooks;

/// <summary>How a provider signs its webhooks and which key/window to verify them with.</summary>
public sealed record WebhookSource(string Provider, string SignatureHeader, string TimestampHeader, string SigningKey, TimeSpan TimestampTolerance);

/// <summary>Body of the <c>fh-webhooks-inbound</c> message: just a pointer to the stored event (ADR-005).</summary>
public sealed record WebhookInboundMessage(Guid WebhookEventId, string Provider);

/// <summary>
/// The inbound webhook pipeline shared by every provider (docs/INTEGRATIONS.md §5): size limit → raw-body HMAC and
/// timestamp check → identify → persist in the inbox (dedup by provider event id) → hand off → always 200 once stored.
/// Hand-off is a queue message when messaging is on; otherwise (or if the broker refuses) the event is processed here.
/// Anonymous by design: the signature authenticates the caller.
/// </summary>
public sealed partial class WebhookReceiver(
    WebhookSignatureVerifier verifier,
    WebhookInbox inbox,
    WebhookEventProcessor processor,
    IMessagePublisher publisher,
    WebhooksMetrics metrics,
    ILogger<WebhookReceiver> logger)
{
    public const string RateLimitPolicy = "webhooks";
    public const int MaxBodyBytes = 64 * 1024;

    public async Task<Results<Ok, ProblemHttpResult, StatusCodeHttpResult>> ReceiveAsync(
        HttpContext httpContext,
        WebhookSource source,
        IWebhookProcessor providerProcessor,
        CancellationToken cancellationToken)
    {
        using var activity = ApplicationTelemetry.ActivitySource.StartActivity("Webhook.Ingest");
        activity?.SetTag("webhook.provider", source.Provider);
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

        var payload = Encoding.UTF8.GetString(body);
        var identity = providerProcessor.Identify(payload);
        if (identity is null)
        {
            metrics.Rejected(source.Provider, "malformed");
            return TypedResults.Problem(title: "Webhook rejected", detail: "Malformed event payload.", statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault();
        var recorded = await inbox.TryRecordAsync(source.Provider, identity.EventId, identity.EventType, payload, correlationId, cancellationToken);

        activity?.SetTag("webhook.event.type", identity.EventType);
        activity?.SetTag("webhook.duplicate", recorded is null);

        if (recorded is null)
        {
            metrics.Duplicate(source.Provider);
            LogDuplicate(source.Provider, identity.EventId);
            return TypedResults.Ok();
        }

        metrics.Received(source.Provider, identity.EventType);

        if (await TryEnqueueAsync(recorded.Id, source.Provider, identity.EventId, correlationId, cancellationToken))
        {
            return TypedResults.Ok();
        }

        // Messaging off or broker unavailable: process now. Failures are recorded on the event, never surfaced.
        await processor.ProcessAsync(recorded.Id, cancellationToken);
        return TypedResults.Ok();
    }

    private async Task<bool> TryEnqueueAsync(Guid webhookEventId, string provider, string providerEventId, string? correlationId, CancellationToken cancellationToken)
    {
        if (!publisher.IsEnabled)
        {
            return false;
        }

        try
        {
            var envelope = new MessageEnvelope(
                webhookEventId.ToString(),
                nameof(WebhookInboundMessage),
                JsonSerializer.Serialize(new WebhookInboundMessage(webhookEventId, provider)),
                DateTimeOffset.UtcNow,
                correlationId,
                System.Diagnostics.Activity.Current?.Id);

            await publisher.PublishAsync(Queues.WebhooksInbound, envelope, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogEnqueueFailed(exception, provider, providerEventId);
            return false;
        }
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

    [LoggerMessage(EventId = 5203, Level = LogLevel.Warning, Message = "Could not enqueue webhook {Provider}/{ProviderEventId}; processing in-process instead")]
    private partial void LogEnqueueFailed(Exception exception, string provider, string providerEventId);
}
