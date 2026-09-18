using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace FulfillmentHub.Api.Middleware;

/// <summary>
/// Accepts or generates an <c>X-Correlation-Id</c>, echoes it on the response and attaches it to the
/// logging scope and the current trace, so one client-reported id maps to one filtered log/trace stream.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request);

        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(HttpRequest request)
    {
        var provided = request.Headers[HeaderName].FirstOrDefault();

        return IsAcceptable(provided) ? provided : Guid.CreateVersion7().ToString();
    }

    private static bool IsAcceptable([NotNullWhen(true)] string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= MaxLength
        && value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
