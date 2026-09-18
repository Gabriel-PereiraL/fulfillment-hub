using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using FulfillmentHub.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// SQS client, queue provisioner/registry and the <see cref="IMessagePublisher"/> (a no-op when
    /// <c>Messaging:Sqs:Enabled</c> is false, so hosts run without a broker).
    /// </summary>
    public static IServiceCollection AddFulfillmentHubMessaging(this IServiceCollection services)
    {
        services.AddOptions<SqsOptions>()
            .BindConfiguration(SqsOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IAmazonSQS>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<SqsOptions>>().Value;
            var config = new AmazonSQSConfig { RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region) };

            if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl; // LocalStack
                config.AuthenticationRegion = options.Region;
            }

            return string.IsNullOrWhiteSpace(options.AccessKey)
                ? new AmazonSQSClient(config) // default credential chain (roles, env, profile)
                : new AmazonSQSClient(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        });

        services.AddSingleton<SqsQueueProvisioner>();
        services.AddSingleton<QueueMetrics>();
        services.AddSingleton<IMessagePublisher>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<SqsOptions>>().Value.Enabled
                ? ActivatorUtilities.CreateInstance<SqsMessagePublisher>(serviceProvider)
                : new NoOpMessagePublisher());

        return services;
    }
}
