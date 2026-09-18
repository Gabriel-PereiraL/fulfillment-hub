using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.Infrastructure.Outbox;

public static class OutboxServiceCollectionExtensions
{
    /// <summary>The publishing side of the outbox (ADR-004): options + processor. The capturing side (interceptor) ships with persistence.</summary>
    public static IServiceCollection AddFulfillmentHubOutboxPublisher(this IServiceCollection services)
    {
        services.AddOptions<OutboxOptions>()
            .BindConfiguration(OutboxOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<OutboxDispatcher>();
        services.AddScoped<OutboxProcessor>();

        return services;
    }
}
