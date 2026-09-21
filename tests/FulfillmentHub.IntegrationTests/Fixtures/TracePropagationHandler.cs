using System.Diagnostics;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Test-only: hosts are wired through <c>TestServer.CreateHandler()</c>, which bypasses the <c>SocketsHttpHandler</c>
/// diagnostics that inject <c>traceparent</c>/<c>baggage</c> in production. This handler does the same injection with
/// the runtime's own <see cref="DistributedContextPropagator"/>, so traces cross the in-process HTTP hops exactly as
/// they cross real ones (BL-127).
/// </summary>
internal sealed class TracePropagationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        DistributedContextPropagator.Current.Inject(Activity.Current, request, static (carrier, name, value) =>
        {
            var message = (HttpRequestMessage)carrier!;
            if (!message.Headers.Contains(name))
            {
                message.Headers.TryAddWithoutValidation(name, value);
            }
        });

        return base.SendAsync(request, cancellationToken);
    }
}
