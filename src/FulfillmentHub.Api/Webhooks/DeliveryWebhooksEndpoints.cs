using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.Infrastructure.Webhooks;
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
        [FromKeyedServices(SimulatedDeliveryProviderClient.Name)] IWebhookProcessor processor,
        IOptions<DeliveryProviderOptions> providerOptions,
        CancellationToken cancellationToken)
    {
        var source = new WebhookSource(
            SimulatedDeliveryProviderClient.Name,
            SignatureHeader,
            TimestampHeader,
            providerOptions.Value.WebhookSigningKey,
            providerOptions.Value.WebhookTimestampTolerance);

        return receiver.ReceiveAsync(httpContext, source, processor, cancellationToken);
    }
}
