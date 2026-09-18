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
            ["Providers:Payment:BaseUrl"] = "http://provider.test",
            ["Providers:Payment:ApiKey"] = "unused",
            ["Providers:Payment:WebhookSigningKey"] = "unused-signing-key-16",
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
        hostedServices.ShouldContain(service => service is PaymentReconciliationService);
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

        // ValidateOnStart reports every invalid options type at once (here Database and Providers:Payment).
        var exception = await Should.ThrowAsync<Exception>(() => host.StartAsync(TestContext.Current.CancellationToken));

        var failures = exception is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [exception];
        failures.ShouldAllBe(e => e is OptionsValidationException);
        failures.ShouldContain(e => e.Message.Contains("ConnectionString"));
    }
}
