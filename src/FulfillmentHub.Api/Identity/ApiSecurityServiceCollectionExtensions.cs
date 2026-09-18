using System.Threading.RateLimiting;
using FulfillmentHub.Application.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
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

        services.AddOptions<RateLimitOptions>()
            .BindConfiguration(RateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimiterOptions>().Configure<IOptions<RateLimitOptions>>((options, limits) =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Credential stuffing / brute force (OWASP A07): a handful of attempts per client IP per minute.
            // Webhooks: generous per-client budget; providers retry with backoff on 429.
            options.AddPolicy(Webhooks.WebhookReceiver.RateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.Value.WebhooksPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.Value.LoginPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
        services.AddRateLimiter(static _ => { });

        return services;
    }
}
