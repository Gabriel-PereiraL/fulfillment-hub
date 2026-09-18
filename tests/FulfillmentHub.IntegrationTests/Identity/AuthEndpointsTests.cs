using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FulfillmentHub.Api.Identity;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Infrastructure.Identity;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace FulfillmentHub.IntegrationTests.Identity;

[Collection(ApiTests.Name)]
public sealed class AuthEndpointsTests(ApiFixture api)
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsBearerToken_AndMeReflectsClaims()
    {
        var user = await api.CreateUserAsync(Password, [Role.Customer], withCustomerProfile: true);
        using var client = api.CreateClientWithOwnAddress();

        var login = await ApiFixture.LoginAsync(client, user.Email.Value, Password);

        login.TokenType.ShouldBe("Bearer");
        login.AccessToken.ShouldNotBeNullOrWhiteSpace();
        login.ExpiresAt.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(login.TokenType, login.AccessToken);
        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/api/v1/me", TestContext.Current.CancellationToken);

        me.ShouldNotBeNull();
        me.UserId.ShouldBe(user.Id.Value);
        me.CustomerId.ShouldNotBeNull();
        me.Roles.ShouldBe(["Customer"]);
    }

    [Fact]
    public async Task Login_UpdatesLastLoginAt()
    {
        var user = await api.CreateUserAsync(Password, [Role.Operator]);
        using var client = api.CreateClientWithOwnAddress();

        await ApiFixture.LoginAsync(client, user.Email.Value, Password);

        using var admin = await api.CreateAuthenticatedClientAsync([Role.Admin]);
        var details = await admin.GetFromJsonAsync<UserDetails>($"/api/v1/users/{user.Id.Value}", TestContext.Current.CancellationToken);
        details.ShouldNotBeNull().LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Login_WrongPassword_UnknownEmail_AndInactiveUser_AreIndistinguishable()
    {
        var user = await api.CreateUserAsync(Password, [Role.Customer]);
        var inactive = await api.CreateUserAsync(Password, [Role.Customer], active: false);
        using var client = api.CreateClientWithOwnAddress();

        var wrongPassword = await Login(client, user.Email.Value, "definitely-not-the-password");
        var unknownEmail = await Login(client, $"{Guid.NewGuid():N}@tests.local", Password);
        var inactiveUser = await Login(client, inactive.Email.Value, Password);

        foreach (var response in new[] { wrongPassword, unknownEmail, inactiveUser })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var bodies = new List<string>();
        foreach (var response in new[] { wrongPassword, unknownEmail, inactiveUser })
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken);
            problem.ShouldNotBeNull();
            bodies.Add($"{problem.Title}|{problem.Detail}|{problem.Extensions["code"]}");
        }

        bodies.Distinct().Count().ShouldBe(1, "every failure must look the same to the caller (no user enumeration)");
        bodies[0].ShouldNotContain(user.Email.Value);
    }

    [Theory]
    [InlineData("""{"email":"not-an-email","password":"x"}""")]
    [InlineData("""{"email":"a@b.co"}""")]
    [InlineData("""{"password":"x"}""")]
    public async Task Login_WithMalformedPayload_ReturnsValidationProblem(string json)
    {
        using var client = api.CreateClientWithOwnAddress();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/v1/auth/login", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Login_IsRateLimitedPerClient()
    {
        using var client = api.CreateClientWithOwnAddress();
        var email = $"{Guid.NewGuid():N}@tests.local";

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            statuses.Add((await Login(client, email, "wrong")).StatusCode);
        }

        statuses.Take(5).ShouldAllBe(status => status == HttpStatusCode.Unauthorized);
        statuses[5].ShouldBe(HttpStatusCode.TooManyRequests);

        // Another client is not affected by this client's budget.
        using var otherClient = api.CreateClientWithOwnAddress();
        (await Login(otherClient, email, "wrong")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401WithChallenge()
    {
        using var client = api.CreateClientWithOwnAddress();

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("Bearer");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedToken_Returns401()
    {
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer]);
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        var tampered = token[..^4] + (token[^4] == 'a' ? "bbbb" : "aaaa");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tampered);

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredToken_Returns401()
    {
        var user = await api.CreateUserAsync(Password, [Role.Customer]);
        var jwt = api.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        var pastClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(-2));
        var expiredToken = new JwtTokenService(Options.Create(jwt), pastClock).Issue(user, null);
        using var client = api.CreateClientWithOwnAddress();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken.Token);

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.ToString().ShouldContain("expired");
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_Returns401()
    {
        var user = await api.CreateUserAsync(Password, [Role.Admin]);
        var foreign = new JwtOptions
        {
            Issuer = ApiFixture.JwtIssuer,
            Audience = ApiFixture.JwtAudience,
            SigningKey = "another-key-that-is-also-at-least-32-bytes-long",
        };
        var forgedToken = new JwtTokenService(Options.Create(foreign), TimeProvider.System).Issue(user, null);
        using var client = api.CreateClientWithOwnAddress();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken.Token);

        var response = await client.GetAsync("/api/v1/me", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthEndpoints_RemainAnonymous()
    {
        using var client = api.CreateClientWithOwnAddress();

        (await client.GetAsync("/health/live", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> Login(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new { email, password }, TestContext.Current.CancellationToken);
}
