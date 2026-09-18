using System.Net.Http.Headers;
using FulfillmentHub.Application.Deliveries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

public static class DeliveryProviderServiceCollectionExtensions
{
    public const string ResiliencePipelineName = "delivery-provider";

    /// <summary>
    /// Typed client for the delivery provider: shared resilience pipeline (outer) + bearer token handler (inner, renews
    /// once on 401), plus a plain named client for the token endpoint. Also binds <c>Fulfillment:Origin</c>, the
    /// pickup side of every quote and delivery.
    /// </summary>
    public static IServiceCollection AddFulfillmentHubDeliveryProvider(this IServiceCollection services)
    {
        services.AddOptions<DeliveryProviderOptions>()
            .BindConfiguration(DeliveryProviderOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FulfillmentOriginOptions>()
            .BindConfiguration(FulfillmentOriginOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<DeliveryAccessTokenProvider>();
        services.AddTransient<DeliveryBearerTokenHandler>();

        services.AddHttpClient(DeliveryAccessTokenProvider.HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<DeliveryProviderOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = options.AttemptTimeout;
        });

        services.AddHttpClient<IDeliveryProviderClient, SimulatedDeliveryProviderClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<DeliveryProviderOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = Timeout.InfiniteTimeSpan; // timeouts are owned by the resilience pipeline
            })
            .AddProviderResilienceHandler<DeliveryProviderOptions>(ResiliencePipelineName)
            .AddHttpMessageHandler<DeliveryBearerTokenHandler>();

        return services;
    }
}
