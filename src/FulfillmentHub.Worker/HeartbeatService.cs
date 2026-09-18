using Microsoft.Extensions.Options;

namespace FulfillmentHub.Worker;

/// <summary>
/// Emits a periodic heartbeat (metric + log) so the worker's liveness is observable even before it has real work.
/// Stops cooperatively on host shutdown.
/// </summary>
public sealed partial class HeartbeatService(
    WorkerMetrics metrics,
    TimeProvider timeProvider,
    IOptions<WorkerOptions> options,
    ILogger<HeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.Value.HeartbeatIntervalSeconds);
        using var timer = new PeriodicTimer(interval, timeProvider);

        LogStarted(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                metrics.Heartbeat();
                LogHeartbeat();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on host shutdown.
        }

        LogStopped();
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Worker heartbeat started with interval {Interval}")]
    private partial void LogStarted(TimeSpan interval);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Worker heartbeat")]
    private partial void LogHeartbeat();

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Worker heartbeat stopped")]
    private partial void LogStopped();
}
