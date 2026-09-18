using System.Threading.RateLimiting;
using FulfillmentHub.Application.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Identity;

public static class ApiSecurityServiceCollectionExtensions
{
    /// <summary>
    /// Bearer authentication (validated with the issuing options), deny-by-default authorization with role policies,
    /// the request-scoped <see cref="ICurrentUser"/> and the login rate limiter.
    /// </summary>
    public static IServiceCollection AddFulfillmentHubApiSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerOptionsSetup>();
        services.AddFulfillmentHubAuthorization();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Credential stuffing / brute force (OWASP A07): a handful of attempts per client IP per minute.
            options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
