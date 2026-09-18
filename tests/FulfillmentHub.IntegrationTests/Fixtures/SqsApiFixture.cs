using FulfillmentHub.Worker.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.LocalStack;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// The API test host with messaging on: a LocalStack container provides SQS, the Worker's provisioning service and
/// consumers run inside the host, so outbox messages and webhooks travel through real queues (ADR-005).
/// Short visibility/backoff and a small receive budget keep dead-letter scenarios fast.
/// </summary>
public sealed class SqsApiFixture : ApiFixture
{
    public const int MaxReceiveCount = 3;

    private readonly LocalStackContainer _localStack = new LocalStackBuilder("localstack/localstack:4")
        .WithEnvironment("SERVICES", "sqs")
        .Build();

    public string SqsEndpoint => _localStack.GetConnectionString();

    public override async ValueTask InitializeAsync()
    {
        await _localStack.StartAsync();
        await base.InitializeAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _localStack.DisposeAsync();
    }

    protected override void ConfigureSettings(IDictionary<string, string?> settings)
    {
        settings["Messaging:Sqs:Enabled"] = "true";
        settings["Messaging:Sqs:ServiceUrl"] = _localStack.GetConnectionString();
        settings["Messaging:Sqs:AccessKey"] = "test";
        settings["Messaging:Sqs:SecretKey"] = "test";
        settings["Messaging:Sqs:QueuePrefix"] = "test-";
        settings["Messaging:Sqs:MaxReceiveCount"] = MaxReceiveCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        settings["Messaging:Sqs:VisibilityTimeoutSeconds"] = "2";
        settings["Messaging:Sqs:WaitTimeSeconds"] = "1";
        settings["Messaging:Sqs:RetryBaseDelaySeconds"] = "1";
        settings["Messaging:Sqs:RetryMaxDelaySeconds"] = "1";
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.AddHostedService<SqsProvisioningService>(); // before the consumers
        services.AddHostedService<DomainEventsConsumer>();
        services.AddHostedService<WebhooksInboundConsumer>();
    }
}

[CollectionDefinition(Name)]
public sealed class SqsTests : ICollectionFixture<SqsApiFixture>
{
    public const string Name = "Sqs";
}
