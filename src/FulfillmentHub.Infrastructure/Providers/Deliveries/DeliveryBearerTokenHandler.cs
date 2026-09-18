using System.Net;
using System.Net.Http.Headers;

namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

/// <summary>
/// Attaches the cached bearer token to every provider call and, on <c>401</c>, renews it once and repeats the request
/// (the token may have been revoked or expired early). A second 401 is returned to the caller: credentials problem.
/// Sits inside the resilience handler, so a renewal never counts as a retry.
/// </summary>
public sealed class DeliveryBearerTokenHandler(DeliveryAccessTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        response.Dispose();
        tokens.Invalidate(token);

        var renewed = await tokens.GetTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed);

        return await base.SendAsync(request, cancellationToken);
    }
}
