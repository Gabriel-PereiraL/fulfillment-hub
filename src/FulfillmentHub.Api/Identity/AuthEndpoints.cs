using FulfillmentHub.Application.Identity;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FulfillmentHub.Api.Identity;

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Auth");

        group.MapPost("/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(LoginRateLimitPolicy)
            .WithName("Login")
            .WithSummary("Authenticates with e-mail and password and returns a short-lived bearer token.")
            .Produces<LoginResult>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Returns the identity and roles of the caller, as read from the bearer token.")
            .Produces<CurrentUserResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<Results<Ok<LoginResult>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        LoginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), cancellationToken);

        return result.Match<Results<Ok<LoginResult>, ProblemHttpResult>>(
            login => TypedResults.Ok(login),
            failure => TypedResults.Problem(
                title: "Authentication failed",
                detail: failure.Message,
                statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = failure.Code }));
    }

    private static Ok<CurrentUserResponse> GetCurrentUser(ICurrentUser currentUser) =>
        TypedResults.Ok(new CurrentUserResponse(
            currentUser.UserId.Value,
            currentUser.CustomerId?.Value,
            currentUser.Roles.Select(r => r.ToString()).ToList()));
}
