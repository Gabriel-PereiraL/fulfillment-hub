using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Webhooks;

/// <summary>
/// Inbound delivery webhooks (<c>event.delivery_status</c>, docs/INTEGRATIONS.md §1.5/§2.4) on top of the shared
/// <see cref="WebhookReceiver"/> pipeline. The provider signs the raw body in <c>X-Uber-Signature</c>.
/// </summary>
public static class DeliveryWebhooksEndpoints
{
    public const string SignatureHeader = "X-Uber-Signature";
    public const string TimestampHeader = "X-Timestamp";
    public const string DeliveryStatusEvent = "event.delivery_status";

    public static IEndpointRouteBuilder MapDeliveryWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/webhooks/deliveries", ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(WebhookReceiver.RateLimitPolicy)
            .WithTags("Webhooks")
            .WithName("ReceiveDeliveryWebhook")
            .WithSummary("Receives event.delivery_status events from the delivery provider (HMAC-signed).")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);

        return app;
    }

    private static Task<Results<Ok, ProblemHttpResult, StatusCodeHttpResult>> ReceiveAsync(
        HttpContext httpContext,
        WebhookReceiver receiver,
        ApplyDeliveryWebhookHandler handler,
        IOptions<DeliveryProviderOptions> providerOptions,
        CancellationToken cancellationToken)
    {
        var source = new WebhookSource(
            SimulatedDeliveryProviderClient.Name,
            SignatureHeader,
            TimestampHeader,
            providerOptions.Value.WebhookSigningKey,
            providerOptions.Value.WebhookTimestampTolerance);

        return receiver.ReceiveAsync<DeliveryWebhookPayload>(
            httpContext,
            source,
            identify: static payload =>
                string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.DeliveryId ?? payload.Data?.Id)
                    ? null
                    : new WebhookIdentity(payload.Id, payload.Kind ?? "unknown"),
            process: async (payload, ct) =>
            {
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
                        payload.DeliveryId ?? payload.Data!.Id!,
                        new ProviderDeliveryEvent(
                            payload.Id!,
                            providerStatus,
                            SimulatedDeliveryProviderClient.MapStatus(providerStatus),
                            payload.Data?.Updated ?? payload.Created ?? DateTimeOffset.UtcNow,
                            SimulatedDeliveryProviderClient.MapCourier(courier?.Name, courier?.VehicleType, courier?.PhoneNumber, courier?.Location?.Lat, courier?.Location?.Lng)));
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

    /// <summary>Wire shape of <c>event.delivery_status</c> (subset); everything optional so bad input is a 400, not a 500.</summary>
    private sealed record DeliveryWebhookPayload(string? Id, string? Kind, DateTimeOffset? Created, string? Status, string? DeliveryId, DeliveryWebhookData? Data);

    private sealed record DeliveryWebhookData(string? Id, string? Status, DateTimeOffset? Updated, DeliveryWebhookCourier? Courier, string? TrackingUrl);

    private sealed record DeliveryWebhookCourier(string? Name, string? VehicleType, string? PhoneNumber, DeliveryWebhookLatLng? Location);

    private sealed record DeliveryWebhookLatLng(double? Lat, double? Lng);
}
