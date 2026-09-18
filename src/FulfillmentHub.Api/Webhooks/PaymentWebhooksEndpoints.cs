using System.Text.Json;
using FulfillmentHub.Api.Middleware;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Webhooks;

/// <summary>
/// Inbound payment webhooks (docs/INTEGRATIONS.md §5). Anonymous by design: the caller is authenticated by the HMAC
/// signature over the raw body plus a timestamp window. The event is persisted (deduplicated by provider event id)
/// before anything else, then applied; the provider always gets a fast 200 once the event is on record.
/// </summary>
public static partial class PaymentWebhooksEndpoints
{
    public const string RateLimitPolicy = "webhooks";
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";
    public const int MaxBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static IEndpointRouteBuilder MapPaymentWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/payments", ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicy)
            .WithTags("Webhooks")
            .WithName("ReceivePaymentWebhook")
            .WithSummary("Receives payment.status_changed events from the payment provider (HMAC-signed).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return app;
    }

    private static async Task<Results<Ok, ProblemHttpResult, StatusCodeHttpResult>> ReceiveAsync(
        HttpContext httpContext,
        WebhookSignatureVerifier verifier,
        WebhookInbox inbox,
        ApplyPaymentWebhookHandler handler,
        IOptions<PaymentProviderOptions> providerOptions,
        PaymentsMetrics metrics,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        const string provider = SimulatedPaymentGatewayClient.Name;
        var request = httpContext.Request;

        if (request.ContentLength > MaxBodyBytes)
        {
            metrics.WebhookRejected(provider, "too_large");
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            metrics.WebhookRejected(provider, "too_large");
            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var signature = verifier.Verify(
            providerOptions.Value.WebhookSigningKey,
            body,
            request.Headers[SignatureHeader].FirstOrDefault(),
            request.Headers[TimestampHeader].FirstOrDefault(),
            providerOptions.Value.WebhookTimestampTolerance);

        if (signature != WebhookSignatureResult.Valid)
        {
            metrics.WebhookRejected(provider, signature.ToString());
            LogRejected(logger, provider, signature);
            return TypedResults.Problem(title: "Webhook rejected", detail: "Invalid or missing signature.", statusCode: StatusCodes.Status401Unauthorized);
        }

        PaymentWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PaymentWebhookPayload>(body, JsonOptions);
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Id) || payload.Data is null || string.IsNullOrWhiteSpace(payload.Data.PaymentId))
        {
            metrics.WebhookRejected(provider, "malformed");
            return TypedResults.Problem(title: "Webhook rejected", detail: "Malformed event payload.", statusCode: StatusCodes.Status400BadRequest);
        }

        var correlationId = httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault();
        var recorded = await inbox.TryRecordAsync(provider, payload.Id, payload.Type ?? "unknown", System.Text.Encoding.UTF8.GetString(body), correlationId, cancellationToken);

        if (recorded is null)
        {
            metrics.WebhookDuplicate(provider);
            LogDuplicate(logger, provider, payload.Id);
            return TypedResults.Ok();
        }

        metrics.WebhookReceived(provider, payload.Type ?? "unknown");

        if (!string.Equals(payload.Type, "payment.status_changed", StringComparison.Ordinal))
        {
            await inbox.MarkIgnoredAsync(recorded.Id, $"unsupported event type '{payload.Type}'", cancellationToken);
            return TypedResults.Ok();
        }

        PaymentWebhookCommand command;
        try
        {
            command = new PaymentWebhookCommand(
                payload.Id,
                payload.Data.PaymentId,
                SimulatedPaymentGatewayClient.MapStatus(payload.Data.Status ?? string.Empty),
                payload.Data.FailureCode,
                payload.Data.OccurredAt ?? payload.CreatedAt ?? DateTimeOffset.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            await inbox.MarkIgnoredAsync(recorded.Id, exception.Message, cancellationToken);
            return TypedResults.Ok();
        }

        // Processing failures are recorded, not surfaced: the event is safely stored and reconciliation will catch up.
        try
        {
            var result = await handler.HandleAsync(command, cancellationToken);

            if (result.IsSuccess)
            {
                await inbox.MarkProcessedAsync(recorded.Id, cancellationToken);
            }
            else
            {
                await inbox.MarkFailedAsync(recorded.Id, $"{result.Failure.Code}: {result.Failure.Message}", cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProcessingFailed(logger, exception, provider, payload.Id);
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

    /// <summary>Wire shape of the provider's event (docs/INTEGRATIONS.md §3); everything optional so bad input is a 400, not a 500.</summary>
    private sealed record PaymentWebhookPayload(string? Id, string? Type, DateTimeOffset? CreatedAt, PaymentWebhookPayloadData? Data);

    private sealed record PaymentWebhookPayloadData(string? PaymentId, string? Status, string? FailureCode, string? OrderReference, DateTimeOffset? OccurredAt);

    [LoggerMessage(EventId = 5200, Level = LogLevel.Warning, Message = "Webhook from {Provider} rejected: {Reason}")]
    private static partial void LogRejected(ILogger logger, string provider, WebhookSignatureResult reason);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information, Message = "Webhook {Provider}/{ProviderEventId} already received; ignoring duplicate")]
    private static partial void LogDuplicate(ILogger logger, string provider, string providerEventId);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "Processing webhook {Provider}/{ProviderEventId} failed")]
    private static partial void LogProcessingFailed(ILogger logger, Exception exception, string provider, string providerEventId);
}
