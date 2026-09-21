using FulfillmentHub.Api.Common;
using FulfillmentHub.Api.Idempotency;
using FulfillmentHub.Api.Identity;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Orders;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Orders;

public static class OrdersEndpoints
{
    public const string PlaceOrderRateLimitPolicy = "place-order";

    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders").WithTags("Orders");

        group.MapPost("/", PlaceOrderAsync)
            .RequireAuthorization(AuthorizationPolicies.CustomerOnly)
            .RequireRateLimiting(PlaceOrderRateLimitPolicy)
            .AddEndpointFilter<IdempotencyFilter<PlaceOrderRequest, OrderDto>>()
            .WithName("PlaceOrder")
            .WithSummary("Places an order for the authenticated customer. Requires an Idempotency-Key header.")
            .WithDescription("Reserves stock and creates the order in one transaction. Repeating the request with the same "
                + "Idempotency-Key and payload replays the original response instead of creating a second order.")
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetOrder")
            .WithSummary("Returns an order. Customers only see their own orders; others are reported as not found.")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListAsync)
            .WithName("ListOrders")
            .WithSummary("Lists orders newest first with keyset pagination (cursor).")
            .Produces<OrderPage>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithName("CancelOrder")
            .WithSummary("Cancels an order and returns its stock. Customers can cancel until the courier picks the parcel up.")
            .Produces<OrderDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<Results<Created<OrderDto>, ProblemHttpResult>> PlaceOrderAsync(
        PlaceOrderRequest request,
        PlaceOrderHandler handler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var address = request.DeliveryAddress;
        var command = new PlaceOrderCommand(
            request.Items.Select(i => new PlaceOrderLine(i.ProductId, i.Quantity)).ToList(),
            new AddressDto(
                address.Street,
                address.Number,
                address.Complement,
                address.District,
                address.City,
                address.State,
                address.PostalCode,
                address.Country ?? "BR",
                address.Latitude,
                address.Longitude),
            httpContext.Request.Headers[IdempotencyFilter<PlaceOrderRequest, OrderDto>.HeaderName].FirstOrDefault());

        // The order is committed together with its OrderPlaced event (ADR-004); the payment is created by the Worker
        // from the outbox, so the response is the order as accepted (Created) and the status catches up asynchronously.
        var result = await handler.HandleAsync(command, cancellationToken);

        return result.Match<Results<Created<OrderDto>, ProblemHttpResult>>(
            order => TypedResults.Created($"/api/v1/orders/{order.Id}", order),
            failure => failure.ToProblem());
    }

    private static async Task<Results<Ok<OrderDto>, NotFound>> GetByIdAsync(
        Guid id,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        var order = await queries.GetByIdAsync(id, cancellationToken);

        return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
    }

    private static async Task<Results<Ok<OrderPage>, ProblemHttpResult>> ListAsync(
        string? cursor,
        int? pageSize,
        Guid? customerId,
        OrderQueries queries,
        CancellationToken cancellationToken)
    {
        var result = await queries.ListAsync(cursor, pageSize, customerId, cancellationToken);

        return result.Match<Results<Ok<OrderPage>, ProblemHttpResult>>(
            page => TypedResults.Ok(page),
            failure => failure.ToProblem());
    }

    private static async Task<Results<Ok<OrderDto>, ProblemHttpResult>> CancelAsync(
        Guid id,
        CancelOrderRequest? request,
        CancelOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CancelOrderCommand(id, request?.Note), cancellationToken);

        return result.Match<Results<Ok<OrderDto>, ProblemHttpResult>>(
            order => TypedResults.Ok(order),
            failure => failure.ToProblem());
    }
}
