using FulfillmentHub.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker.Messaging;

/// <summary>Creates the queues before the consumers start (hosted services start in registration order).</summary>
public sealed class SqsProvisioningService(SqsQueueProvisioner provisioner, IOptions<SqsOptions> options) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        options.Value.Enabled ? provisioner.EnsureQueuesAsync(cancellationToken) : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
