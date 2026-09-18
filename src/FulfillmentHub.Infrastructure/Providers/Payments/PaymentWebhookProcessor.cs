using System.Text.Json;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Infrastructure.Webhooks;

namespace FulfillmentHub.Infrastructure.Providers.Payments;

/// <summary>Identifies and applies <c>payment.status_changed</c> events of the simulated PSP (docs/INTEGRATIONS.md §3).</summary>
public sealed class PaymentWebhookProcessor(ApplyPaymentWebhookHandler handler) : IWebhookProcessor
{
    public const string StatusChangedEvent = "payment.status_changed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public WebhookIdentity? Identify(string payload)
    {
        var parsed = Parse(payload);
        return parsed is null || string.IsNullOrWhiteSpace(parsed.Id) || parsed.Data is null || string.IsNullOrWhiteSpace(parsed.Data.PaymentId)
            ? null
            : new WebhookIdentity(parsed.Id, parsed.Type ?? "unknown");
    }

    public async Task<WebhookOutcome> ProcessAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var payload = Parse(webhookEvent.Payload);
        if (payload?.Data?.PaymentId is null || payload.Id is null)
        {
            return new WebhookOutcome.Ignored("malformed stored payload");
        }

        if (!string.Equals(payload.Type, StatusChangedEvent, StringComparison.Ordinal))
        {
            return new WebhookOutcome.Ignored($"unsupported event type '{payload.Type}'");
        }

        PaymentWebhookCommand command;
        try
        {
            command = new PaymentWebhookCommand(
                payload.Id,
                payload.Data.PaymentId,
                SimulatedPaymentGatewayClient.MapStatus(payload.Data.Status ?? string.Empty),
                payload.Data.FailureCode,
                payload.Data.OccurredAt ?? payload.CreatedAt ?? webhookEvent.ReceivedAt);
        }
        catch (InvalidOperationException exception)
        {
            return new WebhookOutcome.Ignored(exception.Message);
        }

        var result = await handler.HandleAsync(command, cancellationToken);
        return result switch
        {
            { IsSuccess: true } => WebhookOutcome.Done,
            { Failure.Kind: FailureKind.NotFound } => new WebhookOutcome.Ignored(result.Failure.Message),
            _ => new WebhookOutcome.Failed($"{result.Failure.Code}: {result.Failure.Message}"),
        };
    }

    private static PaymentWebhookPayload? Parse(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<PaymentWebhookPayload>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Wire shape of the provider's event; everything optional so bad input is a 400, not a 500.</summary>
    private sealed record PaymentWebhookPayload(string? Id, string? Type, DateTimeOffset? CreatedAt, PaymentWebhookPayloadData? Data);

    private sealed record PaymentWebhookPayloadData(string? PaymentId, string? Status, string? FailureCode, string? OrderReference, DateTimeOffset? OccurredAt);
}
