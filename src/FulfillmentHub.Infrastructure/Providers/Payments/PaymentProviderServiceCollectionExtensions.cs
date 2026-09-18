using System.Net.Http.Headers;
using FulfillmentHub.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Providers.Payments;

public static class PaymentProviderServiceCollectionExtensions
{
    public const string ResiliencePipelineName = "payment-provider";

    /// <summary>Typed client for the payment provider with the shared provider resilience pipeline (docs/INTEGRATIONS.md §4).</summary>
    public static IServiceCollection AddFulfillmentHubPaymentProvider(this IServiceCollection services)
    {
        services.AddOptions<PaymentProviderOptions>()
            .BindConfiguration(PaymentProviderOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IPaymentGatewayClient, SimulatedPaymentGatewayClient>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.Timeout = Timeout.InfiniteTimeSpan; // timeouts are owned by the resilience pipeline
            })
            .AddProviderResilienceHandler<PaymentProviderOptions>(ResiliencePipelineName);

        return services;
    }
}
