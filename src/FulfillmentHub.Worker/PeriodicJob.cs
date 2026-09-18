namespace FulfillmentHub.Worker;

/// <summary>
/// A background job that runs on a fixed interval with one DI scope per run. A failing run is logged and the loop
/// continues: a job error must never stop the worker. Shutdown cancels the wait cleanly.
/// </summary>
public abstract partial class PeriodicJob(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger logger) : BackgroundService
{
    protected abstract string JobName { get; }

    protected abstract TimeSpan Interval { get; }

    /// <summary>One run; returns a one-line summary to log, or null when nothing happened.</summary>
    protected abstract Task<string?> RunAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        LogStarted(JobName, Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var summary = await RunAsync(scope.ServiceProvider, stoppingToken);

                    if (summary is not null)
                    {
                        LogRun(JobName, summary);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogRunFailed(exception, JobName);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "{Job} started (every {Interval})")]
    private partial void LogStarted(string job, TimeSpan interval);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "{Job}: {Summary}")]
    private partial void LogRun(string job, string summary);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "{Job} run failed")]
    private partial void LogRunFailed(Exception exception, string job);
}
