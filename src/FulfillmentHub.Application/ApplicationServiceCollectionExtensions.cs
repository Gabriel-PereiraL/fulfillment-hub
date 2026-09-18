using FulfillmentHub.Application.Catalog;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Application.Payments;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Use cases and queries shared by every host (API and Worker). Ports (persistence, gateway, current user) are
    /// provided by the host/Infrastructure.
    /// </summary>
    public static IServiceCollection AddFulfillmentHubApplication(this IServiceCollection services)
    {
        services.AddSingleton<OrdersMetrics>();
        services.AddScoped<PlaceOrderHandler>();
        services.AddScoped<CancelOrderHandler>();
        services.AddScoped<OrderQueries>();

        services.AddScoped<ProductQueries>();

        services.AddSingleton<PaymentsMetrics>();
        services.AddScoped<PaymentStatusApplier>();
        services.AddScoped<CreatePaymentForOrderHandler>();
        services.AddScoped<ApplyPaymentWebhookHandler>();
        services.AddScoped<ReconcilePaymentsHandler>();

        return services;
    }

    /// <summary>Login and user queries: only the API hosts them, because they need the hasher and the token issuer.</summary>
    public static IServiceCollection AddFulfillmentHubIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<LoginHandler>();
        services.AddScoped<UserQueries>();

        return services;
    }
}
