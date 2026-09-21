using System.Threading.RateLimiting;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Api.Identity;

public static class ApiSecurityServiceCollectionExtensions
{
    /// <summary>
    /// Bearer authentication (validated with the issuing options), deny-by-default authorization with role policies,
    /// the request-scoped <see cref="ICurrentUser"/> and the rate limiters (login, webhooks, order creation).
    /// </summary>
    public static IServiceCollection AddFulfillmentHubApiSecurity(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, JwtBearerOptionsSetup>();
        services.AddFulfillmentHubAuthorization();

        services.AddHttpContextAccessor();
        services.AddSingleton<Idempotency.IdempotencyMetrics>();
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

            // Order creation (D-84): a sliding window per authenticated user, so one account cannot exhaust stock
            // reservations or delivery quotes; the client IP is the fallback for callers that are not authenticated.
            options.AddPolicy(Orders.OrdersEndpoints.PlaceOrderRateLimitPolicy, httpContext =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: httpContext.User.FindFirst(JwtClaimNames.Subject)?.Value is { Length: > 0 } subject
                        ? "user:" + subject
                        : "ip:" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
                    factory: _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = limits.Value.OrdersPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    }));
        });
        services.AddRateLimiter(static _ => { });

        return services;
    }
}
