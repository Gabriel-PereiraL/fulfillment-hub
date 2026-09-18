using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Hosts the provider simulator in-process. Its outgoing webhooks are routed into the API's <c>TestServer</c>, so a
/// test observes the real round trip: API → simulator (HTTP) → settlement → signed webhook → API (HTTP).
/// </summary>
public sealed class ProviderSimulatorFactory(Func<TestServer> apiServer) : WebApplicationFactory<ProviderSimulator.Program>
{
    public const string ApiKey = "integration-tests-payment-api-key";
    public const string WebhookSigningKey = "integration-tests-webhook-signing-key";
    public const string WebhookUrl = "http://api.test/api/v1/webhooks/payments";
    public const string DeliveryClientId = "integration-tests-delivery-client";
    public const string DeliveryClientSecret = "integration-tests-delivery-secret";
    public const string DeliveryCustomerId = "cus_sim_tests";
    public const string DeliveryWebhookSigningKey = "integration-tests-delivery-signing-key";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(ApiFixture.EnvironmentName);
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Simulator:Payments:ApiKey"] = ApiKey,
                ["Simulator:Payments:WebhookSigningKey"] = WebhookSigningKey,
                ["Simulator:Payments:WebhookUrl"] = WebhookUrl,
                ["Simulator:Payments:SettleDelayMs"] = "100",
                ["Simulator:Payments:ApprovalRate"] = "1",
                ["Simulator:Delivery:ClientId"] = DeliveryClientId,
                ["Simulator:Delivery:ClientSecret"] = DeliveryClientSecret,
                ["Simulator:Delivery:CustomerId"] = DeliveryCustomerId,
                ["Simulator:Delivery:WebhookSigningKey"] = DeliveryWebhookSigningKey,
                ["Simulator:Delivery:WebhookUrl"] = "http://api.test/api/v1/webhooks/deliveries",
                ["Simulator:Delivery:CourierAssignMs"] = "500",
                ["Simulator:Delivery:StepMs"] = "500",
            }));
        builder.ConfigureTestServices(services =>
            services.AddHttpClient(WebhookDispatcher.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => apiServer().CreateHandler()));
    }
}
