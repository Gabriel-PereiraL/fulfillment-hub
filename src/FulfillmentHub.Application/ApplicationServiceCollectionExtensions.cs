using FulfillmentHub.Application.Catalog;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>Use cases and queries. Ports (persistence, hashing, tokens, current user) are provided by the host/Infrastructure.</summary>
    public static IServiceCollection AddFulfillmentHubApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginHandler>();
        services.AddScoped<UserQueries>();

        services.AddSingleton<OrdersMetrics>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
        services.AddScoped<OrderQueries>();

        services.AddScoped<ProductQueries>();

        return services;
    }
}
