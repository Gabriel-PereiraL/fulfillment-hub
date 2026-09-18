using System.Net;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace FulfillmentHub.IntegrationTests.Api;

[Collection(ApiTests.Name)]
public sealed class HealthEndpointsTests(ApiFixture api)
{
    [Fact]
    public async Task HealthLive_ReturnsHealthy_WithoutTouchingDependencies()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task HealthReady_ReturnsHealthy_WhenDatabaseIsReachable()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task HealthReady_ReturnsServiceUnavailable_WhenDatabaseIsUnreachable()
    {
        await using var unreachableDatabaseApi = new UnreachableDatabaseApiFactory();
        using var client = unreachableDatabaseApi.CreateClient();

        var liveResponse = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var readyResponse = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        liveResponse.StatusCode.ShouldBe(HttpStatusCode.OK, "liveness must not depend on the database");
        readyResponse.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await readyResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Unhealthy");
    }

    private sealed class UnreachableDatabaseApiFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(ApiFixture.EnvironmentName);
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Port 1 is closed: the connection is refused immediately instead of timing out.
                    ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2",
                    ["Jwt:Issuer"] = ApiFixture.JwtIssuer,
                    ["Jwt:Audience"] = ApiFixture.JwtAudience,
                    ["Jwt:SigningKey"] = ApiFixture.JwtSigningKey,
                    ["Providers:Payment:BaseUrl"] = "http://provider.test",
                    ["Providers:Payment:ApiKey"] = ProviderSimulatorFactory.ApiKey,
                    ["Providers:Payment:WebhookSigningKey"] = ProviderSimulatorFactory.WebhookSigningKey,
                    ["Providers:Delivery:BaseUrl"] = "http://provider.test",
                    ["Providers:Delivery:ClientId"] = ProviderSimulatorFactory.DeliveryClientId,
                    ["Providers:Delivery:ClientSecret"] = ProviderSimulatorFactory.DeliveryClientSecret,
                    ["Providers:Delivery:CustomerId"] = ProviderSimulatorFactory.DeliveryCustomerId,
                    ["Providers:Delivery:WebhookSigningKey"] = ProviderSimulatorFactory.DeliveryWebhookSigningKey,
                }));
        }
    }
}
