using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Test-only <see cref="IOptionsMonitor{TOptions}"/> for the simulator's <see cref="ChaosOptions"/>: a test switches
/// failure injection on for a scenario (BL-150) and back off afterwards, without restarting the in-process host.
/// </summary>
public sealed class TestChaos : IOptionsMonitor<ChaosOptions>
{
    private volatile ChaosOptions _current = new();

    public ChaosOptions CurrentValue => _current;

    public ChaosOptions Get(string? name) => _current;

    public IDisposable? OnChange(Action<ChaosOptions, string?> listener) => null;

    /// <summary>Applies <paramref name="options"/> until the returned handle is disposed (then back to no chaos).</summary>
    public IDisposable Apply(ChaosOptions options)
    {
        _current = options;
        return new Reset(this);
    }

    private sealed class Reset(TestChaos chaos) : IDisposable
    {
        public void Dispose() => chaos._current = new ChaosOptions();
    }
}
