using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Test-only: the in-memory TestServer has no remote address, so the IP-based rate limiter would put every test
/// in the same bucket. Tests announce an address through a header and this middleware makes it the connection's
/// <see cref="ConnectionInfo.RemoteIpAddress"/>. It runs before the application pipeline via <see cref="IStartupFilter"/>.
/// </summary>
internal sealed class TestClientAddressMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Test-Client-Address";

    public Task InvokeAsync(HttpContext context)
    {
        if (IPAddress.TryParse(context.Request.Headers[HeaderName].FirstOrDefault(), out var address))
        {
            context.Connection.RemoteIpAddress = address;
        }

        return next(context);
    }

    internal sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.UseMiddleware<TestClientAddressMiddleware>();
            next(app);
        };
    }
}
