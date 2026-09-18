using FulfillmentHub.Application.Deliveries;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>Re-reads deliveries that have been quiet for too long from the provider (lost webhooks, BL-067).</summary>
public sealed class DeliveryReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<DeliveryReconciliationOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryReconciliationService> logger) : PeriodicJob(scopeFactory, timeProvider, logger)
{
    protected override string JobName => "Delivery reconciliation";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task<string?> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var summary = await services.GetRequiredService<ReconcileDeliveriesHandler>()
            .HandleAsync(options.Value.QuietFor, options.Value.BatchSize, cancellationToken);

        return summary.Checked > 0 ? $"checked {summary.Checked}, corrected {summary.Corrected}" : null;
    }
}
