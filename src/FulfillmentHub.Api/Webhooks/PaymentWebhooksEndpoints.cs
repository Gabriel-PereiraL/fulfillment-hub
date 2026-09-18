using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Webhooks;

/// <summary>Inbound payment webhooks (docs/INTEGRATIONS.md §3/§5) on top of the shared <see cref="WebhookReceiver"/> pipeline.</summary>
public static class PaymentWebhooksEndpoints
{
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";

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
        [FromKeyedServices(SimulatedPaymentGatewayClient.Name)] IWebhookProcessor processor,
        IOptions<PaymentProviderOptions> providerOptions,
        CancellationToken cancellationToken)
    {
        var source = new WebhookSource(
            SimulatedPaymentGatewayClient.Name,
            SignatureHeader,
            TimestampHeader,
            providerOptions.Value.WebhookSigningKey,
            providerOptions.Value.WebhookTimestampTolerance);

        return receiver.ReceiveAsync(httpContext, source, processor, cancellationToken);
    }
}
