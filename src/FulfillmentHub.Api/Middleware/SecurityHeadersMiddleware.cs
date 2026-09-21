namespace FulfillmentHub.Api.Middleware;

/// <summary>
/// Response headers for a JSON API (docs/SECURITY.md §1.5, D-82). The headers are attached when the response starts,
/// so they survive the exception handler and status-code pages re-executing the pipeline. The Development-only
/// OpenAPI/Scalar UI is excluded from the Content-Security-Policy because it is an HTML page with its own scripts.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    private static readonly PathString ApiPrefix = new("/api");
    private static readonly PathString[] UiPrefixes = [new("/scalar"), new("/openapi")];

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            var headers = httpContext.Response.Headers;

            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            if (!IsInteractiveUi(httpContext.Request.Path))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
            }

            // Every API response is dynamic and may carry a token or customer data: never let a proxy or browser cache it.
            if (httpContext.Request.Path.StartsWithSegments(ApiPrefix))
            {
                headers.CacheControl = "no-store";
            }

            return Task.CompletedTask;
        }, context);

        return next(context);
    }

    private static bool IsInteractiveUi(PathString path) =>
        UiPrefixes.Any(prefix => path.StartsWithSegments(prefix));
}
