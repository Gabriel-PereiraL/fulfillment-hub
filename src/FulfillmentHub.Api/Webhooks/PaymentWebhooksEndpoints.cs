using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Infrastructure.Providers.Payments;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Webhooks;

/// <summary>
/// Inbound payment webhooks (docs/INTEGRATIONS.md §3/§5) on top of the shared <see cref="WebhookReceiver"/> pipeline.
/// </summary>
public static class PaymentWebhooksEndpoints
{
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";
    public const string StatusChangedEvent = "payment.status_changed";

    public static IEndpointRouteBuilder MapPaymentWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/payments", ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(WebhookReceiver.RateLimitPolicy)
            .WithTags("Webhooks")
            .WithName("ReceivePaymentWebhook")
            .WithSummary("Receives payment.status_changed events from the payment provider (HMAC-signed).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return app;
    }

    private static Task<Results<Ok, ProblemHttpResult, StatusCodeHttpResult>> ReceiveAsync(
        HttpContext httpContext,
        WebhookReceiver receiver,
        ApplyPaymentWebhookHandler handler,
        IOptions<PaymentProviderOptions> providerOptions,
        CancellationToken cancellationToken)
    {
        var source = new WebhookSource(
            SimulatedPaymentGatewayClient.Name,
            SignatureHeader,
            TimestampHeader,
            providerOptions.Value.WebhookSigningKey,
            providerOptions.Value.WebhookTimestampTolerance);

        return receiver.ReceiveAsync<PaymentWebhookPayload>(
            httpContext,
            source,
            identify: static payload =>
                string.IsNullOrWhiteSpace(payload.Id) || payload.Data is null || string.IsNullOrWhiteSpace(payload.Data.PaymentId)
                    ? null
                    : new WebhookIdentity(payload.Id, payload.Type ?? "unknown"),
            process: async (payload, ct) =>
            {
                if (!string.Equals(payload.Type, StatusChangedEvent, StringComparison.Ordinal))
                {
                    return new WebhookOutcome.Ignored($"unsupported event type '{payload.Type}'");
                }

                PaymentWebhookCommand command;
                try
                {
                    command = new PaymentWebhookCommand(
                        payload.Id!,
                        payload.Data!.PaymentId!,
                        SimulatedPaymentGatewayClient.MapStatus(payload.Data.Status ?? string.Empty),
                        payload.Data.FailureCode,
                        payload.Data.OccurredAt ?? payload.CreatedAt ?? DateTimeOffset.UtcNow);
                }
                catch (InvalidOperationException exception)
                {
                    return new WebhookOutcome.Ignored(exception.Message);
                }

                var result = await handler.HandleAsync(command, ct);
                return result switch
                {
                    { IsSuccess: true } => new WebhookOutcome.Processed(),
                    { Failure.Kind: FailureKind.NotFound } => new WebhookOutcome.Ignored(result.Failure.Message),
                    _ => new WebhookOutcome.Failed($"{result.Failure.Code}: {result.Failure.Message}"),
                };
            },
            cancellationToken);
    }

    /// <summary>Wire shape of the provider's event (docs/INTEGRATIONS.md §3); everything optional so bad input is a 400, not a 500.</summary>
    private sealed record PaymentWebhookPayload(string? Id, string? Type, DateTimeOffset? CreatedAt, PaymentWebhookPayloadData? Data);

    private sealed record PaymentWebhookPayloadData(string? PaymentId, string? Status, string? FailureCode, string? OrderReference, DateTimeOffset? OccurredAt);
}
