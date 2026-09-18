using FulfillmentHub.Application.Deliveries;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>
/// Requests a delivery for every paid order that has none yet (D-52: a poll stands in for the outbox until Phase 8).
/// One DI scope per run; a failing run is logged and the loop continues.
/// </summary>
public sealed partial class DeliveryRequestService(
    IServiceScopeFactory scopeFactory,
    IOptions<DeliveryRequestOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryRequestService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.Interval, timeProvider);

        LogStarted(settings.Interval);

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

    private async Task RunOnceAsync(DeliveryRequestOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<RequestPendingDeliveriesHandler>();
            var summary = await handler.HandleAsync(settings.BatchSize, cancellationToken);

            if (summary.Candidates > 0)
            {
                LogRun(summary.Candidates, summary.Requested, summary.Deferred);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogRunFailed(exception);
        }
    }

    [LoggerMessage(EventId = 2200, Level = LogLevel.Information, Message = "Delivery requests started (every {Interval})")]
    private partial void LogStarted(TimeSpan interval);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Information, Message = "Delivery requests: {Candidates} paid orders, {Requested} requested, {Deferred} deferred")]
    private partial void LogRun(int candidates, int requested, int deferred);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Error, Message = "Delivery request run failed")]
    private partial void LogRunFailed(Exception exception);
}
