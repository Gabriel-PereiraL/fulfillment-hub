using System.Text.Json;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Infrastructure.Webhooks;

namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

/// <summary>Identifies and applies <c>event.delivery_status</c> events of the Uber-like simulator (docs/INTEGRATIONS.md §1.5/§2.4).</summary>
public sealed class DeliveryWebhookProcessor(ApplyDeliveryWebhookHandler handler) : IWebhookProcessor
{
    public const string DeliveryStatusEvent = "event.delivery_status";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public WebhookIdentity? Identify(string payload)
    {
        var parsed = Parse(payload);
        return parsed is null || string.IsNullOrWhiteSpace(parsed.Id) || string.IsNullOrWhiteSpace(parsed.DeliveryId ?? parsed.Data?.Id)
            ? null
            : new WebhookIdentity(parsed.Id, parsed.Kind ?? "unknown");
    }

    public async Task<WebhookOutcome> ProcessAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken)
    {
        var payload = Parse(webhookEvent.Payload);
        var providerDeliveryId = payload?.DeliveryId ?? payload?.Data?.Id;
        if (payload?.Id is null || providerDeliveryId is null)
        {
            return new WebhookOutcome.Ignored("malformed stored payload");
        }

        if (!string.Equals(payload.Kind, DeliveryStatusEvent, StringComparison.Ordinal))
        {
            return new WebhookOutcome.Ignored($"unsupported event kind '{payload.Kind}'");
        }

        var providerStatus = payload.Data?.Status ?? payload.Status ?? string.Empty;
        DeliveryWebhookCommand command;
        try
        {
            var courier = payload.Data?.Courier;
            command = new DeliveryWebhookCommand(
                providerDeliveryId,
                new ProviderDeliveryEvent(
                    payload.Id,
                    providerStatus,
                    SimulatedDeliveryProviderClient.MapStatus(providerStatus),
                    payload.Data?.Updated ?? payload.Created ?? webhookEvent.ReceivedAt,
                    SimulatedDeliveryProviderClient.MapCourier(courier?.Name, courier?.VehicleType, courier?.PhoneNumber, courier?.Location?.Lat, courier?.Location?.Lng)));
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

    private static DeliveryWebhookPayload? Parse(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<DeliveryWebhookPayload>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Wire shape of <c>event.delivery_status</c> (subset); everything optional so bad input is a 400, not a 500.</summary>
    private sealed record DeliveryWebhookPayload(string? Id, string? Kind, DateTimeOffset? Created, string? Status, string? DeliveryId, DeliveryWebhookData? Data);

    private sealed record DeliveryWebhookData(string? Id, string? Status, DateTimeOffset? Updated, DeliveryWebhookCourier? Courier, string? TrackingUrl);

    private sealed record DeliveryWebhookCourier(string? Name, string? VehicleType, string? PhoneNumber, DeliveryWebhookLatLng? Location);

    private sealed record DeliveryWebhookLatLng(double? Lat, double? Lng);
}
