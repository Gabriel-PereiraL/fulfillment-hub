using FulfillmentHub.Application.Payments;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>Periodically reconciles pending payments with the provider (docs/INTEGRATIONS.md §5, T18).</summary>
public sealed class PaymentReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReconciliationOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentReconciliationService> logger) : PeriodicJob(scopeFactory, timeProvider, logger)
{
    protected override string JobName => "Payment reconciliation";

    protected override TimeSpan Interval => options.Value.Interval;

    protected override async Task<string?> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var summary = await services.GetRequiredService<ReconcilePaymentsHandler>()
            .HandleAsync(options.Value.PendingFor, options.Value.BatchSize, cancellationToken);

        return summary.Checked > 0 || summary.Retried > 0
            ? $"checked {summary.Checked}, corrected {summary.Corrected}, retried {summary.Retried}"
            : null;
    }
}
