using FulfillmentHub.Application.Catalog;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Catalog;

public static class ProductsEndpoints
{
    public static IEndpointRouteBuilder MapProductsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/products").WithTags("Products");

        group.MapGet("/", ListAsync)
            .WithName("ListProducts")
            .WithSummary("Lists the active products with current stock.")
            .Produces<IReadOnlyList<ProductDto>>();

        return app;
    }

    private static async Task<Ok<IReadOnlyList<ProductDto>>> ListAsync(ProductQueries queries, CancellationToken cancellationToken) =>
        TypedResults.Ok(await queries.ListActiveAsync(cancellationToken));
}
