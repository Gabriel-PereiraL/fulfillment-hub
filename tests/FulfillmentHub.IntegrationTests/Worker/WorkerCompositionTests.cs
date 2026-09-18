using FulfillmentHub.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.IntegrationTests.Worker;

public sealed class WorkerCompositionTests
{
    [Fact]
    public void WorkerHost_CanResolveEveryHostedService_WithValidatedGraph()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testing",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Host=localhost;Database=unused;Username=u;Password=p",
        });

        builder.AddFulfillmentHubWorker();

        // Same checks the Development host performs on Build(): captive dependencies and unresolvable constructors.
        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        var hostedServices = provider.GetServices<IHostedService>().ToList();

        hostedServices.ShouldContain(service => service is HeartbeatService);
    }

    [Fact]
    public async Task WorkerHost_FailsFast_WhenDatabaseConnectionStringIsMissing()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = "Testing",
        });

        builder.AddFulfillmentHubWorker();
        using var host = builder.Build();

        var exception = await Should.ThrowAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("ConnectionString");
    }
}
