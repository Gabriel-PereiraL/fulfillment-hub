using System.Net;
using System.Text;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Test switch in front of every provider call the API/Worker makes. <see cref="Enabled"/> makes providers
/// "unreachable" (503 without reaching the simulator); <see cref="Script"/> lets a test answer specific requests
/// itself (e.g. force <c>expired_quote</c>). Both are reset by the tests that use them.
/// </summary>
public sealed class ProviderOutage
{
    private int _enabled;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) == 1;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    /// <summary>Returns a canned response for a request, or null to let it through to the simulator.</summary>
    public Func<HttpRequestMessage, HttpResponseMessage?>? Script { get; set; }

    public int RejectedCalls { get; private set; }

    public static HttpResponseMessage ProviderError(HttpStatusCode status, string code, string message = "scripted by the test") =>
        new(status)
        {
            Content = new StringContent($$"""{"code":"{{code}}","message":"{{message}}","kind":"error"}""", Encoding.UTF8, "application/json"),
        };

    internal sealed class Handler(ProviderOutage outage) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (outage.Enabled)
            {
                outage.RejectedCalls++;
                return Task.FromResult(ProviderError(HttpStatusCode.ServiceUnavailable, "service_unavailable", "simulated outage"));
            }

            if (outage.Script?.Invoke(request) is { } scripted)
            {
                return Task.FromResult(scripted);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
