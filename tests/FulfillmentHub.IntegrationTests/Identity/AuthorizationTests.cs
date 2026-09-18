using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.IntegrationTests.Identity;

[Collection(ApiTests.Name)]
public sealed class AuthorizationTests(ApiFixture api)
{
    [Fact]
    public async Task AdminOnlyEndpoint_AllowsAdmin()
    {
        var target = await api.CreateUserAsync("correct horse battery staple", [Role.Customer]);
        using var admin = await api.CreateAuthenticatedClientAsync([Role.Admin]);

        var response = await admin.GetAsync($"/api/v1/users/{target.Id.Value}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<UserDetails>(TestContext.Current.CancellationToken);
        details.ShouldNotBeNull();
        details.Email.ShouldBe(target.Email.Value);
        details.Roles.ShouldBe(["Customer"]);
    }

    [Theory]
    [InlineData(Role.Customer)]
    [InlineData(Role.Operator)]
    public async Task AdminOnlyEndpoint_RejectsOtherRoles_With403(Role role)
    {
        using var client = await api.CreateAuthenticatedClientAsync([role]);

        var response = await client.GetAsync($"/api/v1/users/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminOnlyEndpoint_WithoutToken_Returns401()
    {
        using var client = api.CreateClientWithOwnAddress();

        var response = await client.GetAsync($"/api/v1/users/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminOnlyEndpoint_UnknownUser_Returns404()
    {
        using var admin = await api.CreateAuthenticatedClientAsync([Role.Admin]);

        var response = await admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Api_RefusesToStart_WhenJwtSigningKeyIsTooShort()
    {
        using var factory = new ShortKeyApiFactory(api.ConnectionString);

        var act = () => factory.CreateClient();

        var exception = act.ShouldThrow<OptionsValidationException>();
        exception.Message.ShouldContain(nameof(FulfillmentHub.Infrastructure.Identity.JwtOptions.SigningKey));
    }

    private sealed class ShortKeyApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(ApiFixture.EnvironmentName);
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                    ["Jwt:Issuer"] = ApiFixture.JwtIssuer,
                    ["Jwt:Audience"] = ApiFixture.JwtAudience,
                    ["Jwt:SigningKey"] = "too-short",
                }));
        }
    }
}
