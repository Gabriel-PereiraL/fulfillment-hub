using FulfillmentHub.Application;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.Infrastructure.Telemetry;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FulfillmentHub.Worker;

public static class WorkerHostBuilderExtensions
{
    /// <summary>
    /// Composition root of the worker host, kept out of <c>Program.cs</c> so tests can validate the DI graph.
    /// </summary>
    public static IHostApplicationBuilder AddFulfillmentHubWorker(this IHostApplicationBuilder builder)
    {
        builder.AddFulfillmentHubTelemetry("fulfillmenthub-worker");
        builder.Services.AddFulfillmentHubPersistence();
        builder.Services.AddFulfillmentHubPaymentProvider();
        builder.Services.AddFulfillmentHubDeliveryProvider();
        builder.Services.AddFulfillmentHubApplication();
        builder.Services.AddFulfillmentHubOutboxPublisher();
        builder.Services.AddScoped<ICurrentUser, AnonymousCurrentUser>();

        builder.Services.AddOptions<ReconciliationOptions>()
            .BindConfiguration(ReconciliationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<DeliveryRequestOptions>()
            .BindConfiguration(DeliveryRequestOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<DeliveryReconciliationOptions>()
            .BindConfiguration(DeliveryReconciliationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<OutboxPublisherOptions>()
            .BindConfiguration(OutboxPublisherOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<WorkerOptions>()
            .BindConfiguration(WorkerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<WorkerMetrics>();
        builder.Services.AddHostedService<HeartbeatService>();
        builder.Services.AddHostedService<OutboxPublisherService>();
        builder.Services.AddHostedService<PaymentReconciliationService>();
        builder.Services.AddHostedService<DeliveryRequestService>();
        builder.Services.AddHostedService<DeliveryReconciliationService>();

        return builder;
    }
}
