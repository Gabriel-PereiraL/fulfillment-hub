using FulfillmentHub.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace FulfillmentHub.Api.Identity;

/// <summary>
/// Named policies used by endpoints. The fallback policy makes every endpoint require an authenticated user unless
/// it explicitly opts out with <c>AllowAnonymous()</c> (deny by default).
/// </summary>
public static class AuthorizationPolicies
{
    public const string CustomerOnly = nameof(CustomerOnly);
    public const string OperatorOrAdmin = nameof(OperatorOrAdmin);
    public const string AdminOnly = nameof(AdminOnly);

    public static IServiceCollection AddFulfillmentHubAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(CustomerOnly, policy => policy.RequireRole(nameof(Role.Customer)))
            .AddPolicy(OperatorOrAdmin, policy => policy.RequireRole(nameof(Role.Operator), nameof(Role.Admin)))
            .AddPolicy(AdminOnly, policy => policy.RequireRole(nameof(Role.Admin)))
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
