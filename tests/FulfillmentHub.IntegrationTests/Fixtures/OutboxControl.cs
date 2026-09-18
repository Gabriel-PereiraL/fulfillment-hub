using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// The test host runs the Worker's outbox publisher so flows progress on their own, like in production. Tests that
/// need to drive a step by hand pause it (<see cref="Pause"/>) and call <see cref="RunOnceAsync"/> themselves.
/// </summary>
public sealed class OutboxControl(IServiceScopeFactory scopeFactory)
{
    private int _paused = 1; // starts paused: the fixture starts it once the schema exists

    public bool Paused => Volatile.Read(ref _paused) == 1;

    public void Start() => Volatile.Write(ref _paused, 0);

    /// <summary>Stops the background publisher until the returned handle is disposed.</summary>
    public IDisposable Pause()
    {
        Volatile.Write(ref _paused, 1);
        return new Resume(this);
    }

    /// <summary>One publisher pass, exactly what the Worker does on each tick.</summary>
    public async Task<OutboxBatchSummary> RunOnceAsync()
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Resume(OutboxControl control) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref control._paused, 0);
    }

    /// <summary>The Worker's publisher loop, gated by <see cref="OutboxControl"/>.</summary>
    internal sealed class PausablePublisher(
        IServiceScopeFactory scopeFactory,
        OutboxControl control,
        TimeProvider timeProvider,
        ILogger<PausablePublisher> logger) : PeriodicJob(scopeFactory, timeProvider, logger)
    {
        protected override string JobName => "Outbox publisher (tests)";

        protected override TimeSpan Interval => TimeSpan.FromMilliseconds(200);

        protected override async Task<string?> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            if (control.Paused)
            {
                return null;
            }

            var summary = await services.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(cancellationToken);
            return summary.Claimed > 0 ? $"claimed {summary.Claimed}, processed {summary.Processed}, retried {summary.Retried}, failed {summary.Failed}" : null;
        }
    }
}
