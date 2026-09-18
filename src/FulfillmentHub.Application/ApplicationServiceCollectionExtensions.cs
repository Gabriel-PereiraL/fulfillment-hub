using FulfillmentHub.Application.Catalog;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Application.Outbox;
using FulfillmentHub.Application.Outbox.Handlers;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Domain.Orders.Events;
using FulfillmentHub.Domain.Payments.Events;
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

        services.AddSingleton<WebhooksMetrics>();
        services.AddSingleton<PaymentsMetrics>();
        services.AddScoped<PaymentStatusApplier>();
        services.AddScoped<CreatePaymentForOrderHandler>();
        services.AddScoped<ApplyPaymentWebhookHandler>();
        services.AddScoped<ReconcilePaymentsHandler>();

        services.AddSingleton<DeliveriesMetrics>();
        services.AddScoped<CheckoutDeliveryQuoter>();
        services.AddScoped<RequestDeliveryHandler>();
        services.AddScoped<RequestPendingDeliveriesHandler>();
        services.AddScoped<DeliveryStatusApplier>();
        services.AddScoped<ApplyDeliveryWebhookHandler>();
        services.AddScoped<ReconcileDeliveriesHandler>();

        services.AddScoped<RefundPaymentHandler>();
        services.AddSingleton<OutboxMetrics>();
        services.AddKeyedScoped<IOutboxHandler, OrderPlacedHandler>(nameof(OrderPlaced));
        services.AddKeyedScoped<IOutboxHandler, OrderPaidHandler>(nameof(OrderPaid));
        services.AddKeyedScoped<IOutboxHandler, OrderCancelledHandler>(nameof(OrderCancelled));
        services.AddKeyedScoped<IOutboxHandler, PaymentPaidHandler>(nameof(PaymentPaid));

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
