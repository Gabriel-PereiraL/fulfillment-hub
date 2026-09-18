using System.Diagnostics;
using System.Net;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Infrastructure.Providers.Deliveries;
using FulfillmentHub.UnitTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.UnitTests.Deliveries;

/// <summary>
/// Runs the real delivery client pipeline (token handler + resilience) against a scripted transport: token cache and
/// renewal, the retry matrix of docs/INTEGRATIONS.md §1.4 (T9) and the circuit breaker opening and closing (T10).
/// </summary>
public sealed class DeliveryProviderClientTests
{
    private const string TokenJson = """{"access_token":"tok_1","token_type":"Bearer","expires_in":300,"scope":"eats.deliveries"}""";
    private const string SecondTokenJson = """{"access_token":"tok_2","token_type":"Bearer","expires_in":300,"scope":"eats.deliveries"}""";
    private const string DeliveryJson = """{"kind":"delivery","id":"del_1","quote_id":"dqt_1","status":"pending","complete":false,"courier":null,"courier_imminent":false,"created":"2026-09-18T12:00:00Z","updated":"2026-09-18T12:00:00Z","currency":"brl","fee":1450,"tracking_url":"https://simulator.local/track/del_1","external_id":"x","live_mode":false,"uuid":"u"}""";
    private const string QuoteJson = """{"kind":"delivery_quote","id":"dqt_1","created":"2026-09-18T12:00:00Z","expires":"2026-09-18T12:15:00Z","fee":1450,"currency":"brl","currency_type":"BRL","dropoff_eta":"2026-09-18T12:40:00Z","duration":40,"pickup_duration":15,"dropoff_deadline":"2026-09-18T13:10:00Z"}""";

    [Fact]
    public async Task Token_IsFetchedOnce_AndReusedAcrossCalls()
    {
        var transport = new ScriptedTransport()
            .RespondTo("oauth/token", HttpStatusCode.OK, TokenJson)
            .RespondTo("/deliveries/del_1", HttpStatusCode.OK, DeliveryJson)
            .RespondTo("/deliveries/del_1", HttpStatusCode.OK, DeliveryJson);
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var first = await client.GetAsync("del_1", TestContext.Current.CancellationToken);
        var second = await client.GetAsync("del_1", TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        second.IsSuccess.ShouldBeTrue();
        transport.CountRequests("oauth/token").ShouldBe(1, "the token is cached");
        var tokenRequest = transport.Requests.Single(r => r.Uri!.PathAndQuery.Contains("oauth/token", StringComparison.Ordinal));
        tokenRequest.Body.ShouldContain("grant_type=client_credentials");
        tokenRequest.Body.ShouldContain("client_secret=test-secret");
        transport.Requests.Where(r => r.Uri!.PathAndQuery.Contains("/deliveries/", StringComparison.Ordinal))
            .ShouldAllBe(r => r.Headers.Authorization!.Parameter == "tok_1");
    }

    [Fact]
    public async Task Unauthorized_RenewsTheTokenOnce_AndRepeatsTheCall()
    {
        var transport = new ScriptedTransport()
            .RespondTo("oauth/token", HttpStatusCode.OK, TokenJson)
            .RespondTo("/deliveries/del_1", HttpStatusCode.Unauthorized, """{"code":"unauthorized","message":"expired","kind":"error"}""")
            .RespondTo("oauth/token", HttpStatusCode.OK, SecondTokenJson)
            .RespondTo("/deliveries/del_1", HttpStatusCode.OK, DeliveryJson);
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var result = await client.GetAsync("del_1", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        transport.CountRequests("oauth/token").ShouldBe(2);
        transport.Requests.Last().Headers.Authorization!.Parameter.ShouldBe("tok_2");
    }

    [Fact]
    public async Task Quote_MapsCentsAndCurrency_AndSendsStructuredAddresses()
    {
        var transport = new ScriptedTransport()
            .RespondTo("oauth/token", HttpStatusCode.OK, TokenJson)
            .RespondTo("delivery_quotes", HttpStatusCode.OK, QuoteJson);
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var result = await client.QuoteAsync(new GatewayQuoteRequest(Party("Store", "50000-000"), Party("Customer", "50100-000"), null), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Fee.ShouldBe(Money.Of(14.50m));
        result.Value.ProviderQuoteId.ShouldBe("dqt_1");
        result.Value.DurationMinutes.ShouldBe(40);
        var sent = transport.Requests.Last();
        sent.Uri!.PathAndQuery.ShouldBe("/delivery/v1/customers/cus_test/delivery_quotes");
        sent.Body.ShouldContain("\"pickup_address\":\"{");
        sent.Body.ShouldContain("zip_code");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "expired_quote", FailureKind.Validation)]
    [InlineData(HttpStatusCode.BadRequest, "address_undeliverable", FailureKind.Validation)]
    [InlineData(HttpStatusCode.NotFound, "delivery_not_found", FailureKind.NotFound)]
    [InlineData(HttpStatusCode.PaymentRequired, "customer_suspended", FailureKind.Forbidden)]
    public async Task ContractErrors_AreNeverRetried_AndKeepTheProviderCode(HttpStatusCode status, string code, FailureKind expectedKind)
    {
        var transport = new ScriptedTransport()
            .RespondTo("oauth/token", HttpStatusCode.OK, TokenJson)
            .RespondTo("/deliveries", status, $$"""{"code":"{{code}}","message":"nope","kind":"error"}""");
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var result = await client.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.Kind.ShouldBe(expectedKind);
        result.Failure.Code.ShouldBe("provider." + code);
        transport.CountRequests("/deliveries").ShouldBe(1);
    }

    [Fact]
    public async Task DuplicateDelivery_CarriesTheExistingDeliveryId()
    {
        var transport = new ScriptedTransport()
            .RespondTo("oauth/token", HttpStatusCode.OK, TokenJson)
            .RespondTo("/deliveries", HttpStatusCode.Conflict, """{"code":"duplicate_delivery","message":"exists","kind":"error","metadata":{"delivery_id":"del_existing"}}""");
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var result = await client.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.Kind.ShouldBe(FailureKind.Conflict);
        result.Failure.Metadata.ShouldNotBeNull()[IDeliveryProviderClient.DuplicateDeliveryIdKey].ShouldBe("del_existing");
    }

    [Fact]
    public async Task TooManyRequests_IsRetried_HonouringRetryAfter()
    {
        var transport = new ScriptedTransport()
            .RespondTo("oauth/token", HttpStatusCode.OK, TokenJson)
            .RespondTo("/deliveries/del_1", HttpStatusCode.TooManyRequests, """{"code":"customer_limited","message":"slow","kind":"error"}""", retryAfterSeconds: 1)
            .RespondTo("/deliveries/del_1", HttpStatusCode.OK, DeliveryJson);
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var stopwatch = Stopwatch.StartNew();
        var result = await client.GetAsync("del_1", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        transport.CountRequests("/deliveries/del_1").ShouldBe(2);
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task CouriersBusy_IsRetriedUpToTheLimit_ThenUnavailable()
    {
        var transport = new ScriptedTransport().RespondTo("oauth/token", HttpStatusCode.OK, TokenJson);
        for (var i = 0; i < 4; i++)
        {
            transport.RespondTo("/deliveries", HttpStatusCode.ServiceUnavailable, """{"code":"couriers_busy","message":"busy","kind":"error"}""");
        }

        await using var provider = BuildProvider(transport, maxRetryAttempts: 3);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        var result = await client.CreateAsync(CreateRequest(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.Kind.ShouldBe(FailureKind.Unavailable);
        result.Failure.Code.ShouldBe("provider.couriers_busy");
        transport.CountRequests("/deliveries").ShouldBe(4, "1 attempt + 3 retries, every one with the same idempotency key");
        transport.Requests.Where(r => r.Uri!.PathAndQuery.EndsWith("/deliveries", StringComparison.Ordinal))
            .Select(r => r.Body).Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task CircuitBreaker_Opens_AfterSustainedFailures_AndClosesAgain_AfterASuccessfulProbe()
    {
        var transport = new ScriptedTransport().RespondTo("oauth/token", HttpStatusCode.OK, TokenJson);
        for (var i = 0; i < 10; i++)
        {
            transport.RespondTo("/deliveries/del_1", HttpStatusCode.InternalServerError, """{"code":"internal_server_error","message":"boom","kind":"error"}""");
        }

        transport.RespondTo("/deliveries/del_1", HttpStatusCode.OK, DeliveryJson);
        transport.RespondTo("/deliveries/del_1", HttpStatusCode.OK, DeliveryJson);

        await using var provider = BuildProvider(transport, maxRetryAttempts: 0, circuitBreakSeconds: 1);
        var client = provider.GetRequiredService<IDeliveryProviderClient>();

        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync("del_1", TestContext.Current.CancellationToken)).Failure!.Kind.ShouldBe(FailureKind.Unavailable);
        }

        var whileOpen = await client.GetAsync("del_1", TestContext.Current.CancellationToken);
        whileOpen.Failure!.Message.ShouldContain("BrokenCircuit");
        transport.CountRequests("/deliveries/del_1").ShouldBe(10, "an open circuit fails fast");

        await Task.Delay(TimeSpan.FromSeconds(1.2), TestContext.Current.CancellationToken);

        var probe = await client.GetAsync("del_1", TestContext.Current.CancellationToken);
        var afterClose = await client.GetAsync("del_1", TestContext.Current.CancellationToken);

        probe.IsSuccess.ShouldBeTrue("half-open lets one probe through");
        afterClose.IsSuccess.ShouldBeTrue("a successful probe closes the circuit");
        transport.CountRequests("/deliveries/del_1").ShouldBe(12);
    }

    private static ServiceProvider BuildProvider(ScriptedTransport transport, int maxRetryAttempts = 3, int circuitBreakSeconds = 30)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:Delivery:BaseUrl"] = "http://provider.test",
            ["Providers:Delivery:ClientId"] = "test-client",
            ["Providers:Delivery:ClientSecret"] = "test-secret",
            ["Providers:Delivery:CustomerId"] = "cus_test",
            ["Providers:Delivery:WebhookSigningKey"] = "test-delivery-signing-key",
            ["Providers:Delivery:MaxRetryAttempts"] = maxRetryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Providers:Delivery:RetryBaseDelayMs"] = "10",
            ["Providers:Delivery:CircuitBreakDurationSeconds"] = circuitBreakSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Fulfillment:Origin:Name"] = "Store",
            ["Fulfillment:Origin:Phone"] = "+5581999990001",
            ["Fulfillment:Origin:Street"] = "Rua A",
            ["Fulfillment:Origin:Number"] = "1",
            ["Fulfillment:Origin:District"] = "Centro",
            ["Fulfillment:Origin:City"] = "Recife",
            ["Fulfillment:Origin:State"] = "PE",
            ["Fulfillment:Origin:PostalCode"] = "50000-000",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(TimeProvider.System);
        services.AddFulfillmentHubDeliveryProvider();
        services.AddHttpClient<IDeliveryProviderClient, SimulatedDeliveryProviderClient>().ConfigurePrimaryHttpMessageHandler(() => transport);
        services.AddHttpClient(DeliveryAccessTokenProvider.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => transport);

        return services.BuildServiceProvider();
    }

    private static DeliveryParty Party(string name, string postalCode) =>
        new(name, Address.Create("Rua A", "1", null, "Centro", "Recife", "PE", postalCode, "BR", null, null), PhoneNumber.Of("+5581999990000"));

    private static GatewayCreateDelivery CreateRequest() => new(
        "order-1-delivery-1",
        "dqt_1",
        Party("Store", "50000-000"),
        Party("Customer", "50100-000"),
        [new GatewayManifestItem("Mug", 1, Money.Of(39.5m))],
        "FH-1000",
        Money.Of(39.5m));
}
