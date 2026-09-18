using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FulfillmentHub.UnitTests.Support;

public sealed record RecordedRequest(HttpMethod Method, Uri? Uri, HttpRequestHeaders Headers, string Body);

/// <summary>
/// Fake HTTP transport for provider client tests: answers scripted responses (globally, or per path when a matcher is
/// given) and records every request, so the real resilience pipeline runs without any network.
/// </summary>
public sealed class ScriptedTransport : HttpMessageHandler
{
    private readonly Queue<(Func<HttpRequestMessage, bool> Matches, HttpStatusCode Status, string Body, int? RetryAfter)> _responses = new();

    public List<RecordedRequest> Requests { get; } = [];

    public TimeSpan Delay { get; init; }

    /// <summary>Response for any request, consumed in order.</summary>
    public ScriptedTransport Respond(HttpStatusCode status, string body, int? retryAfterSeconds = null) =>
        RespondWhen(static _ => true, status, body, retryAfterSeconds);

    /// <summary>Response for the next request whose path contains <paramref name="pathContains"/>.</summary>
    public ScriptedTransport RespondTo(string pathContains, HttpStatusCode status, string body, int? retryAfterSeconds = null) =>
        RespondWhen(r => r.RequestUri?.PathAndQuery.Contains(pathContains, StringComparison.Ordinal) == true, status, body, retryAfterSeconds);

    public ScriptedTransport RespondWhen(Func<HttpRequestMessage, bool> matches, HttpStatusCode status, string body, int? retryAfterSeconds = null)
    {
        _responses.Enqueue((matches, status, body, retryAfterSeconds));
        return this;
    }

    public int CountRequests(string pathContains) =>
        Requests.Count(r => r.Uri?.PathAndQuery.Contains(pathContains, StringComparison.Ordinal) == true);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri, request.Headers, body));

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        var (status, responseBody, retryAfter) = Dequeue(request);

        var response = new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8, "application/json") };
        if (retryAfter is { } seconds)
        {
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }

        return response;
    }

    private (HttpStatusCode, string, int?) Dequeue(HttpRequestMessage request)
    {
        // First scripted response whose matcher accepts this request, preserving order for the rest.
        var pending = _responses.ToList();
        var index = pending.FindIndex(r => r.Matches(request));

        if (index < 0)
        {
            return (HttpStatusCode.InternalServerError, """{"code":"unscripted","message":"no scripted response"}""", null);
        }

        var chosen = pending[index];
        pending.RemoveAt(index);
        _responses.Clear();
        foreach (var item in pending)
        {
            _responses.Enqueue(item);
        }

        return (chosen.Status, chosen.Body, chosen.RetryAfter);
    }
}
