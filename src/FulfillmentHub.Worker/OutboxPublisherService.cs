using FulfillmentHub.Infrastructure.Outbox;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>Publishes outbox messages to their in-process handlers (ADR-004); the only place side effects of domain events run.</summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxPublisherOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxPublisherService> logger) : PeriodicJob(scopeFactory, timeProvider, logger)
{
    protected override string JobName => "Outbox publisher";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task<string?> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var summary = await services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(cancellationToken);

        return summary.Claimed > 0
            ? $"claimed {summary.Claimed}, processed {summary.Processed}, retried {summary.Retried}, failed {summary.Failed}"
            : null;
    }
}
