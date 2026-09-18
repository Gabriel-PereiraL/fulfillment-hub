using System.Diagnostics.Metrics;
using FulfillmentHub.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FulfillmentHub.UnitTests.Worker;

public sealed class HeartbeatServiceTests
{
    [Fact]
    public async Task EmitsOneHeartbeatPerInterval_AndStopsCooperatively()
    {
        var time = new TimerAwareFakeTimeProvider();
        using var meterFactory = new TestMeterFactory();
        var metrics = new WorkerMetrics(meterFactory);
        var options = Options.Create(new WorkerOptions { HeartbeatIntervalSeconds = 10 });
        using var service = new HeartbeatService(metrics, time, options, NullLogger<HeartbeatService>.Instance);

        long heartbeats = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "fh.worker.heartbeats")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref heartbeats, value));
        listener.Start();

        await service.StartAsync(TestContext.Current.CancellationToken);

        // BackgroundService.StartAsync schedules ExecuteAsync on the thread pool (.NET 10): wait until the loop
        // has actually armed its PeriodicTimer before advancing the clock, otherwise the first tick is lost.
        await time.FirstTimerCreated.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        for (var tick = 1; tick <= 3; tick++)
        {
            time.Advance(TimeSpan.FromSeconds(10));
            await WaitForHeartbeatsAsync(() => Interlocked.Read(ref heartbeats), expected: tick);
        }

        await service.StopAsync(TestContext.Current.CancellationToken);

        Interlocked.Read(ref heartbeats).ShouldBe(3);
        var executeTask = service.ExecuteTask.ShouldNotBeNull();
        executeTask.IsCompletedSuccessfully.ShouldBeTrue("the loop must exit cleanly on shutdown");
    }

    private static async Task WaitForHeartbeatsAsync(Func<long> current, long expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (current() < expected)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class TimerAwareFakeTimeProvider : FakeTimeProvider
    {
        private readonly TaskCompletionSource _firstTimerCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstTimerCreated => _firstTimerCreated.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            _firstTimerCreated.TrySetResult();
            return timer;
        }
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name, options.Version, options.Tags, scope: this);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
