using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Infrastructure.Outbox;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.Infrastructure.Providers.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FulfillmentHub.IntegrationTests.Fixtures;

/// <summary>
/// Hosts the API in-process against a real PostgreSQL container with migrations applied, plus the provider simulator
/// (<see cref="ProviderSimulatorFactory"/>) reachable through the API's payment <c>HttpClient</c>.
/// Shared by every test in the <see cref="ApiTests"/> collection (one container per test run).
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string EnvironmentName = "Testing";
    public const string JwtIssuer = "fulfillmenthub-tests";
    public const string JwtAudience = "fulfillmenthub-api-tests";
    public const string JwtSigningKey = "integration-tests-signing-key-with-at-least-32-bytes";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly ProviderSimulatorFactory _simulator;
    private readonly ProviderOutage _providerOutage = new();

    public ApiFixture()
    {
        _simulator = new ProviderSimulatorFactory(() => Server);
    }

    public string ConnectionString => _postgres.GetConnectionString();

    public ProviderSimulatorFactory Simulator => _simulator;

    /// <summary>Flip <see cref="ProviderOutage.Enabled"/> to make the provider answer 503 to the API.</summary>
    public ProviderOutage ProviderOutage => _providerOutage;

    /// <summary>Pause/drive the outbox publisher hosted in the test API.</summary>
    public OutboxControl Outbox => Services.GetRequiredService<OutboxControl>();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        await dbContext.Database.MigrateAsync();
        Outbox.Start(); // the hosted publisher waits for the schema
    }

    public override async ValueTask DisposeAsync()
    {
        await _simulator.DisposeAsync();
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>A client with its own IP address, so per-client rate limits never bleed between tests.</summary>
    public HttpClient CreateClientWithOwnAddress()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestClientAddressMiddleware.HeaderName, RandomLoopbackAddress());
        return client;
    }

    public async Task<User> CreateUserAsync(string password, Role[] roles, bool active = true, bool withCustomerProfile = false)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = DateTimeOffset.UtcNow;

        var user = User.Create(EmailAddress.Of($"{Guid.NewGuid():N}@tests.local"), hasher.Hash(password), roles, now);
        if (!active)
        {
            user.Deactivate();
        }

        db.Users.Add(user);

        if (withCustomerProfile)
        {
            db.Customers.Add(Customer.Register(user.Id, "Test Customer", user.Email, PhoneNumber.Of("+5581999990000"), now));
        }

        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<LoginResult> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<LoginResult>())!;
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(Role[] roles, bool withCustomerProfile = false)
    {
        const string password = "correct horse battery staple";
        var user = await CreateUserAsync(password, roles, withCustomerProfile: withCustomerProfile);
        var client = CreateClientWithOwnAddress();
        var login = await LoginAsync(client, user.Email.Value, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(login.TokenType, login.AccessToken);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = _postgres.GetConnectionString(),
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["Jwt:SigningKey"] = JwtSigningKey,
                ["Providers:Payment:BaseUrl"] = "http://provider.test",
                ["Providers:Payment:ApiKey"] = ProviderSimulatorFactory.ApiKey,
                ["Providers:Payment:WebhookSigningKey"] = ProviderSimulatorFactory.WebhookSigningKey,
                ["Providers:Payment:RetryBaseDelayMs"] = "10",
                ["Providers:Payment:CircuitBreakDurationSeconds"] = "1",
                ["Providers:Delivery:BaseUrl"] = "http://provider.test",
                ["Providers:Delivery:ClientId"] = ProviderSimulatorFactory.DeliveryClientId,
                ["Providers:Delivery:ClientSecret"] = ProviderSimulatorFactory.DeliveryClientSecret,
                ["Providers:Delivery:CustomerId"] = ProviderSimulatorFactory.DeliveryCustomerId,
                ["Providers:Delivery:WebhookSigningKey"] = ProviderSimulatorFactory.DeliveryWebhookSigningKey,
                ["Providers:Delivery:RetryBaseDelayMs"] = "10",
                ["Providers:Delivery:CircuitBreakDurationSeconds"] = "1",
                ["Outbox:MaxAttempts"] = "3",
                ["Outbox:BaseDelaySeconds"] = "1",
                ["Outbox:MaxDelaySeconds"] = "2",
            }));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, TestClientAddressMiddleware.StartupFilter>();

            // Run the Worker's outbox publisher inside the test API so flows progress like in production (pausable per test).
            services.AddFulfillmentHubOutboxPublisher();
            services.AddSingleton<OutboxControl>();
            services.AddHostedService<OutboxControl.PausablePublisher>();

            // Same as the Development default: unreadable bodies throw and must be turned into 400 by our exception handler.
            services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

            // The API reaches the simulator hosted in-process: the full HTTP pipeline of both hosts runs, without sockets.
            services.AddHttpClient<IPaymentGatewayClient, SimulatedPaymentGatewayClient>().ConfigurePrimaryHttpMessageHandler(ToSimulator);
            services.AddHttpClient<IDeliveryProviderClient, SimulatedDeliveryProviderClient>().ConfigurePrimaryHttpMessageHandler(ToSimulator);
            services.AddHttpClient(DeliveryAccessTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(ToSimulator);
        });
    }

    private HttpMessageHandler ToSimulator() => new ProviderOutage.Handler(_providerOutage) { InnerHandler = _simulator.Server.CreateHandler() };

    private static string RandomLoopbackAddress()
    {
        var bytes = new byte[3];
        Random.Shared.NextBytes(bytes);
        return new IPAddress([127, bytes[0], bytes[1], (byte)Math.Max(1, (int)bytes[2])]).ToString();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiTests : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
