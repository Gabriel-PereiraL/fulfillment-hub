using System.Net;
using System.Net.Http.Headers;
using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Payments;

/// <summary>
/// Minimal payment-provider contract (docs/INTEGRATIONS.md §3): create (idempotent), get, refund.
/// Not modelled on any specific PSP; exists to exercise FulfillmentHub's resilience and webhook handling.
/// </summary>
public static class PaymentSimulatorEndpoints
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapPaymentSimulatorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/payments/v1")
            .WithTags("Payments simulator")
            .AddEndpointFilter<ApiKeyFilter>()
            .AddEndpointFilter<ChaosFilter>();

        group.MapPost("/payments", CreatePayment).WithName("SimulatorCreatePayment");
        group.MapGet("/payments/{id}", GetPayment).WithName("SimulatorGetPayment");
        group.MapPost("/payments/{id}/refunds", Refund).WithName("SimulatorRefundPayment");

        return app;
    }

    private static IResult CreatePayment(CreatePaymentRequest request, HttpRequest http, PaymentSimulatorStore store)
    {
        var key = http.Headers[IdempotencyHeader].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(key) || key.Length > 128)
        {
            return ProviderErrors.Problem(HttpStatusCode.BadRequest, "invalid_request", "The Idempotency-Key header is required.");
        }

        var (outcome, payment) = store.Create(key, request);

        return outcome switch
        {
            PaymentSimulatorStore.CreateOutcome.Created => Results.Created($"/payments/v1/payments/{payment.Id}", payment.ToResponse()),
            PaymentSimulatorStore.CreateOutcome.Replayed => Results.Ok(payment.ToResponse()),
            _ => ProviderErrors.Problem(HttpStatusCode.Conflict, "idempotency_conflict", "This Idempotency-Key was used with different parameters."),
        };
    }

    private static Results<Ok<PaymentResponse>, JsonHttpResult<ProviderError>> GetPayment(string id, PaymentSimulatorStore store)
    {
        var payment = store.Find(id);

        return payment is null
            ? TypedResults.Json(new ProviderError("not_found", "The requested payment does not exist."), statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(payment.ToResponse());
    }

    private static IResult Refund(string id, RefundRequest? request, PaymentSimulatorStore store)
    {
        if (store.Find(id) is null)
        {
            return ProviderErrors.Problem(HttpStatusCode.NotFound, "not_found", "The requested payment does not exist.");
        }

        var refund = store.Refund(id, request?.Amount);

        return refund is null
            ? ProviderErrors.Problem(HttpStatusCode.UnprocessableEntity, "not_refundable", "Only paid payments can be refunded.")
            : Results.Ok(refund);
    }

    /// <summary>Bearer API key check — a stand-in for the provider's credentials.</summary>
    private sealed class ApiKeyFilter(IOptionsMonitor<PaymentSimulatorOptions> options) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var header = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();

            if (!AuthenticationHeaderValue.TryParse(header, out var auth)
                || !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(auth.Parameter, options.CurrentValue.ApiKey, StringComparison.Ordinal))
            {
                return ProviderErrors.Problem(HttpStatusCode.Unauthorized, "unauthorized", "Invalid credentials provided.");
            }

            return await next(context);
        }
    }
}
