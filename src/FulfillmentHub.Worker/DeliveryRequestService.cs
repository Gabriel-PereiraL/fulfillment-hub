using FulfillmentHub.Application.Deliveries;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>Requests a delivery for every paid order that has none yet (D-52: a poll stands in for the outbox until Phase 8).</summary>
public sealed class DeliveryRequestService(
    IServiceScopeFactory scopeFactory,
    IOptions<DeliveryRequestOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryRequestService> logger) : PeriodicJob(scopeFactory, timeProvider, logger)
{
    protected override string JobName => "Delivery requests";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task<string?> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var summary = await services.GetRequiredService<RequestPendingDeliveriesHandler>()
            .HandleAsync(options.Value.BatchSize, cancellationToken);

        return summary.Candidates > 0
            ? $"{summary.Candidates} paid orders, {summary.Requested} requested, {summary.Deferred} deferred"
            : null;
    }
}
