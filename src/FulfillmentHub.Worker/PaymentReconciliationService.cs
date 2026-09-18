using FulfillmentHub.Application.Payments;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>
/// Periodically reconciles pending payments with the provider (docs/INTEGRATIONS.md §5, T18). One DI scope per
/// run; failures are logged and the loop continues — a reconciliation error must never stop the worker.
/// </summary>
public sealed partial class PaymentReconciliationService(
    IServiceScopeFactory scopeFactory,
    IOptions<ReconciliationOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.Interval, timeProvider);

        LogStarted(settings.Interval, settings.PendingFor);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(settings, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task RunOnceAsync(ReconciliationOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ReconcilePaymentsHandler>();
            var summary = await handler.HandleAsync(settings.PendingFor, settings.BatchSize, cancellationToken);

            if (summary.Checked > 0 || summary.Retried > 0)
            {
                LogRun(summary.Checked, summary.Corrected, summary.Retried);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRunFailed(exception);
        }
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "Payment reconciliation started (every {Interval}, payments pending for {PendingFor})")]
    private partial void LogStarted(TimeSpan interval, TimeSpan pendingFor);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Payment reconciliation: checked {Checked}, corrected {Corrected}, retried {Retried}")]
    private partial void LogRun(int @checked, int corrected, int retried);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "Payment reconciliation run failed")]
    private partial void LogRunFailed(Exception exception);
}
