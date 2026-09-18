using System.Net;
using System.Net.Http.Headers;
using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Deliveries;

/// <summary>
/// Subset of the Uber Direct API contract (docs/INTEGRATIONS.md §2.1): token, quote, create, get, cancel.
/// Educational simulator; it does not connect to Uber infrastructure.
/// </summary>
public static class DeliverySimulatorEndpoints
{
    public static IEndpointRouteBuilder MapDeliverySimulatorEndpoints(this IEndpointRouteBuilder app)
    {
        // The real token endpoint lives on a separate host; here it is just outside the versioned API group.
        app.MapPost("/delivery/oauth/token", IssueToken)
            .WithName("SimulatorDeliveryToken")
            .DisableAntiforgery()
            .AddEndpointFilter<ChaosFilter>()
            .WithMetadata(new RateLimitErrorCode("customer_limited"));

        var group = app.MapGroup("/delivery/v1/customers/{customerId}")
            .WithTags("Delivery simulator")
            .AddEndpointFilter<BearerTokenFilter>()
            .AddEndpointFilter<CustomerFilter>()
            .AddEndpointFilter<ChaosFilter>()
            .WithMetadata(new RateLimitErrorCode("customer_limited"));

        group.MapPost("/delivery_quotes", CreateQuote).WithName("SimulatorCreateQuote");
        group.MapPost("/deliveries", CreateDelivery).WithName("SimulatorCreateDelivery");
        group.MapGet("/deliveries/{id}", GetDelivery).WithName("SimulatorGetDelivery");
        group.MapPost("/deliveries/{id}/cancel", CancelDelivery).WithName("SimulatorCancelDelivery");

        return app;
    }

    private static IResult IssueToken(HttpRequest request, DeliveryTokenIssuer issuer)
    {
        if (!request.HasFormContentType)
        {
            return Results.Json(new { error = "invalid_request", error_description = "Expected application/x-www-form-urlencoded." }, statusCode: StatusCodes.Status400BadRequest);
        }

        var form = request.Form;
        var token = issuer.Issue(form["client_id"], form["client_secret"], form["grant_type"]);

        return token is null
            ? Results.Json(new { error = "invalid_client", error_description = "Client authentication failed." }, statusCode: StatusCodes.Status401Unauthorized)
            : Results.Ok(token);
    }

    private static IResult CreateQuote(DeliveryQuoteRequest request, DeliverySimulatorStore store, IOptionsMonitor<DeliverySimulatorOptions> options)
    {
        if (CouriersBusy(options.CurrentValue))
        {
            return ProviderErrors.Problem(HttpStatusCode.ServiceUnavailable, "couriers_busy", "All couriers are currently busy.");
        }

        var (outcome, quote) = store.Quote(request);

        return outcome switch
        {
            DeliverySimulatorStore.QuoteOutcome.Created => Results.Ok(quote!.ToResponse()),
            DeliverySimulatorStore.QuoteOutcome.AddressUndeliverable => ProviderErrors.Problem(HttpStatusCode.BadRequest, "address_undeliverable", "The specified location is not in a deliverable area."),
            _ => ProviderErrors.Problem(HttpStatusCode.BadRequest, "invalid_params", "pickup_address and dropoff_address must be JSON structured addresses."),
        };
    }

    private static IResult CreateDelivery(CreateDeliveryRequest request, DeliverySimulatorStore store, IOptionsMonitor<DeliverySimulatorOptions> options)
    {
        if (CouriersBusy(options.CurrentValue))
        {
            return ProviderErrors.Problem(HttpStatusCode.ServiceUnavailable, "couriers_busy", "All couriers are currently busy.");
        }

        var (outcome, delivery) = store.Create(request);

        return outcome switch
        {
            DeliverySimulatorStore.CreateOutcome.Created => Results.Ok(delivery!.ToResponse()),
            DeliverySimulatorStore.CreateOutcome.DuplicateDelivery => Results.Json(
                new { code = "duplicate_delivery", message = "An active delivery like this already exists.", kind = "error", metadata = new { delivery_id = delivery!.Id } },
                statusCode: StatusCodes.Status409Conflict),
            DeliverySimulatorStore.CreateOutcome.ExpiredQuote => ProviderErrors.Problem(HttpStatusCode.BadRequest, "expired_quote", "The quote has expired."),
            DeliverySimulatorStore.CreateOutcome.UsedQuote => ProviderErrors.Problem(HttpStatusCode.BadRequest, "used_quote", "The quote was already used."),
            DeliverySimulatorStore.CreateOutcome.QuoteNotFound => ProviderErrors.Problem(HttpStatusCode.BadRequest, "invalid_params", "quote_id does not exist."),
            DeliverySimulatorStore.CreateOutcome.AddressUndeliverable => ProviderErrors.Problem(HttpStatusCode.BadRequest, "address_undeliverable", "The specified location is not in a deliverable area."),
            _ => ProviderErrors.Problem(HttpStatusCode.BadRequest, "invalid_params", "pickup_address and dropoff_address must be JSON structured addresses."),
        };
    }

    private static IResult GetDelivery(string id, DeliverySimulatorStore store)
    {
        var delivery = store.Find(id);

        return delivery is null
            ? ProviderErrors.Problem(HttpStatusCode.NotFound, "delivery_not_found", "The requested delivery does not exist.")
            : Results.Ok(delivery.ToResponse());
    }

    private static IResult CancelDelivery(string id, DeliverySimulatorStore store)
    {
        var (outcome, delivery) = store.Cancel(id);

        return outcome switch
        {
            DeliverySimulatorStore.CancelOutcome.Cancelled => Results.Ok(delivery!.ToResponse()),
            DeliverySimulatorStore.CancelOutcome.Noncancelable => ProviderErrors.Problem(HttpStatusCode.BadRequest, "noncancelable_delivery", "The delivery can no longer be cancelled."),
            _ => ProviderErrors.Problem(HttpStatusCode.NotFound, "delivery_not_found", "The requested delivery does not exist."),
        };
    }

    private static bool CouriersBusy(DeliverySimulatorOptions settings) =>
        settings.CouriersBusyRate > 0 && Random.Shared.NextDouble() < settings.CouriersBusyRate;

    private sealed class BearerTokenFilter(DeliveryTokenIssuer issuer) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var header = context.HttpContext.Request.Headers.Authorization.FirstOrDefault();

            if (!AuthenticationHeaderValue.TryParse(header, out var auth)
                || !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                || !issuer.IsValid(auth.Parameter))
            {
                return ProviderErrors.Problem(HttpStatusCode.Unauthorized, "unauthorized", "Invalid or expired access token.");
            }

            return await next(context);
        }
    }

    private sealed class CustomerFilter(IOptionsMonitor<DeliverySimulatorOptions> options) : IEndpointFilter
    {
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var customerId = context.HttpContext.GetRouteValue("customerId") as string;

            if (!string.Equals(customerId, options.CurrentValue.CustomerId, StringComparison.Ordinal))
            {
                return ProviderErrors.Problem(HttpStatusCode.NotFound, "customer_not_found", "The customer does not exist.");
            }

            return await next(context);
        }
    }
}
