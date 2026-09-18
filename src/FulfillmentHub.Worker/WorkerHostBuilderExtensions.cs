using FulfillmentHub.Infrastructure.Persistence;
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

        builder.Services.AddOptions<WorkerOptions>()
            .BindConfiguration(WorkerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<WorkerMetrics>();
        builder.Services.AddHostedService<HeartbeatService>();

        return builder;
    }
}
