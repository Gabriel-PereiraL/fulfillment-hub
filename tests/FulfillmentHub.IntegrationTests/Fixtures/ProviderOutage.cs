using System.Net;
using System.Text;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Test switch that makes the payment provider "unreachable" from the API's point of view: while enabled every call
/// gets a 503 without reaching the simulator. Lets tests exercise the retry/reconciliation paths deterministically.
/// </summary>
public sealed class ProviderOutage
{
    private int _enabled;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) == 1;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    public int RejectedCalls { get; private set; }

    internal sealed class Handler(ProviderOutage outage) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!outage.Enabled)
            {
                return base.SendAsync(request, cancellationToken);
            }

            outage.RejectedCalls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("""{"code":"service_unavailable","message":"simulated outage","kind":"error"}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
