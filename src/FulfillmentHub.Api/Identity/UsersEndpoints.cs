using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Identity;

/// <summary>User administration. Restricted to administrators by policy on the whole group.</summary>
public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization(AuthorizationPolicies.AdminOnly);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetUserById")
            .WithSummary("Returns a user's identity, roles and status (administrators only).")
            .Produces<UserDetails>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<Results<Ok<UserDetails>, NotFound>> GetByIdAsync(
        Guid id,
        UserQueries queries,
        CancellationToken cancellationToken)
    {
        var user = await queries.GetByIdAsync(UserId.From(id), cancellationToken);

        return user is null ? TypedResults.NotFound() : TypedResults.Ok(user);
    }
}
