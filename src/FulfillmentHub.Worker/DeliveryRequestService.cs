using FulfillmentHub.Application.Deliveries;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>Safety-net sweep behind the <c>OrderPaid</c> outbox handler: requests a delivery for any paid order still without one (D-68).</summary>
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
