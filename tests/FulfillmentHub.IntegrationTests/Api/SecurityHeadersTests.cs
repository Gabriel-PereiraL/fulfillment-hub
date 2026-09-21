using System.Net;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.IntegrationTests.Fixtures;

namespace FulfillmentHub.IntegrationTests.Api;

/// <summary>Response hardening for a JSON API (docs/SECURITY.md §1.5, D-82).</summary>
[Collection(ApiTests.Name)]
public sealed class SecurityHeadersTests(ApiFixture api)
{
    [Fact]
    public async Task ApiResponses_CarrySecurityHeaders_AndAreNotCacheable()
    {
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer]);

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "Referrer-Policy").ShouldBe("no-referrer");
        Header(response, "Content-Security-Policy").ShouldBe("default-src 'none'; frame-ancestors 'none'");
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task ErrorResponses_ProducedByThePipeline_AlsoCarrySecurityHeaders()
    {
        using var client = api.CreateClient();

        // 401 from the fallback policy: no endpoint ran, the headers still come from the middleware.
        var unauthorized = await client.GetAsync("/api/v1/orders", TestContext.Current.CancellationToken);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        Header(unauthorized, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(unauthorized, "Content-Security-Policy").ShouldBe("default-src 'none'; frame-ancestors 'none'");

        // 400 re-executed through the exception handler (unreadable body) keeps them as well.
        using var authenticated = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var content = new StringContent("{not json", System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = content };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var malformed = await authenticated.SendAsync(request, TestContext.Current.CancellationToken);
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        Header(malformed, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(malformed, "X-Frame-Options").ShouldBe("DENY");
    }

    [Fact]
    public async Task HealthEndpoint_AlsoCarriesSecurityHeaders()
    {
        using var client = api.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Header(response, "X-Content-Type-Options").ShouldBe("nosniff");
        Header(response, "X-Frame-Options").ShouldBe("DENY");
        Header(response, "Content-Security-Policy").ShouldBe("default-src 'none'; frame-ancestors 'none'");
    }

    [Fact]
    public async Task HttpsRequests_OutsideDevelopment_ReceiveStrictTransportSecurity()
    {
        // The fixture runs as "Testing" (not Development), so UseHsts() is registered. HSTS is only emitted over HTTPS
        // and never for localhost (the middleware's default exclusion), hence the fictional host name.
        using var client = api.CreateClient();
        client.BaseAddress = new Uri("https://api.fulfillmenthub.test");

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue("HSTS header expected on HTTPS responses outside Development");
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;
}
