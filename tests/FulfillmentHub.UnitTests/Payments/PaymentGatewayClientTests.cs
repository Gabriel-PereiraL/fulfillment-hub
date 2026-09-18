using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Providers.Payments;
using FulfillmentHub.UnitTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.UnitTests.Payments;

/// <summary>
/// Exercises the real resilience pipeline (retry, timeouts, circuit breaker) against a scripted transport, proving
/// the retry matrix of docs/INTEGRATIONS.md §4 (T9/T10) without any network.
/// </summary>
public sealed class PaymentGatewayClientTests
{
    private const string PaymentJson = """{"id":"pay_1","status":"pending","amount":1000,"currency":"brl","order_reference":"o1","failure_code":null,"created_at":"2026-09-18T12:00:00Z","updated_at":"2026-09-18T12:00:00Z"}""";

    [Fact]
    public async Task Create_SendsIdempotencyKeyAndBearer_AndMapsResponse()
    {
        var transport = new ScriptedTransport().Respond(HttpStatusCode.Created, PaymentJson);
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IPaymentGatewayClient>();

        var result = await client.CreateAsync(new GatewayCreatePayment("order-abc", Money.Of(10m), "o1", "c1"), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ProviderPaymentId.ShouldBe("pay_1");
        result.Value.Status.ShouldBe(PaymentStatus.Pending);
        var sent = transport.Requests.ShouldHaveSingleItem();
        sent.Headers.GetValues("Idempotency-Key").ShouldBe(["order-abc"]);
        sent.Headers.Authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "test-api-key"));
        sent.Body.ShouldContain("\"amount\":1000");
        sent.Body.ShouldContain("\"order_reference\":\"o1\"");
    }

    [Fact]
    public async Task TooManyRequests_WithRetryAfter_IsRetriedAfterTheHint()
    {
        var transport = new ScriptedTransport()
            .Respond(HttpStatusCode.TooManyRequests, """{"code":"rate_limited","message":"slow down"}""", retryAfterSeconds: 1)
            .Respond(HttpStatusCode.OK, PaymentJson);
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IPaymentGatewayClient>();

        var stopwatch = Stopwatch.StartNew();
        var result = await client.GetAsync("pay_1", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        transport.Requests.Count.ShouldBe(2);
        stopwatch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900), "Retry-After must be honoured");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, FailureKind.Validation)]
    [InlineData(HttpStatusCode.UnprocessableEntity, FailureKind.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, FailureKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, FailureKind.NotFound)]
    [InlineData(HttpStatusCode.Conflict, FailureKind.Conflict)]
    public async Task ContractErrors_AreNeverRetried_AndMapToPermanentFailures(HttpStatusCode status, FailureKind expectedKind)
    {
        var transport = new ScriptedTransport().Respond(status, """{"code":"some_error","message":"nope"}""");
        await using var provider = BuildProvider(transport);
        var client = provider.GetRequiredService<IPaymentGatewayClient>();

        var result = await client.GetAsync("pay_1", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.Kind.ShouldBe(expectedKind);
        result.Failure.Code.ShouldBe("provider.some_error");
        transport.Requests.Count.ShouldBe(1, "4xx contract errors are not retried");
    }

    [Fact]
    public async Task ServerErrors_AreRetriedUpToTheLimit_ThenReportedAsUnavailable()
    {
        var transport = new ScriptedTransport();
        for (var i = 0; i < 4; i++)
        {
            transport.Respond(HttpStatusCode.ServiceUnavailable, """{"code":"service_unavailable","message":"down"}""");
        }

        await using var provider = BuildProvider(transport, maxRetryAttempts: 3);
        var client = provider.GetRequiredService<IPaymentGatewayClient>();

        var result = await client.GetAsync("pay_1", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.Kind.ShouldBe(FailureKind.Unavailable);
        transport.Requests.Count.ShouldBe(4, "1 attempt + 3 retries");
    }

    [Fact]
    public async Task HangingProvider_HitsAttemptTimeout_IsRetried_ThenUnavailable()
    {
        var transport = new ScriptedTransport { Delay = TimeSpan.FromSeconds(10) }
            .Respond(HttpStatusCode.OK, PaymentJson)
            .Respond(HttpStatusCode.OK, PaymentJson);
        await using var provider = BuildProvider(transport, maxRetryAttempts: 1, attemptTimeoutSeconds: 1, totalTimeoutSeconds: 10);
        var client = provider.GetRequiredService<IPaymentGatewayClient>();

        var result = await client.GetAsync("pay_1", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Failure.Kind.ShouldBe(FailureKind.Unavailable);
        transport.Requests.Count.ShouldBe(2, "the timed-out attempt is retried once");
    }

    [Fact]
    public async Task CircuitBreaker_Opens_AfterSustainedFailures_AndFailsFastWithoutCallingTheProvider()
    {
        var transport = new ScriptedTransport();
        for (var i = 0; i < 20; i++)
        {
            transport.Respond(HttpStatusCode.InternalServerError, """{"code":"internal_server_error","message":"boom"}""");
        }

        await using var provider = BuildProvider(transport, maxRetryAttempts: 0);
        var client = provider.GetRequiredService<IPaymentGatewayClient>();

        for (var i = 0; i < 10; i++)
        {
            (await client.GetAsync("pay_1", TestContext.Current.CancellationToken)).Failure!.Kind.ShouldBe(FailureKind.Unavailable);
        }

        var callsBeforeOpen = transport.Requests.Count;
        var whileOpen = await client.GetAsync("pay_1", TestContext.Current.CancellationToken);

        callsBeforeOpen.ShouldBe(10);
        whileOpen.Failure!.Kind.ShouldBe(FailureKind.Unavailable);
        whileOpen.Failure.Message.ShouldContain("BrokenCircuit");
        transport.Requests.Count.ShouldBe(10, "an open circuit fails fast without a network call");
    }

    private static ServiceProvider BuildProvider(
        ScriptedTransport transport,
        int maxRetryAttempts = 3,
        int attemptTimeoutSeconds = 5,
        int totalTimeoutSeconds = 30)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Providers:Payment:BaseUrl"] = "http://provider.test",
            ["Providers:Payment:ApiKey"] = "test-api-key",
            ["Providers:Payment:WebhookSigningKey"] = "test-webhook-signing-key",
            ["Providers:Payment:MaxRetryAttempts"] = maxRetryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Providers:Payment:RetryBaseDelayMs"] = "10",
            ["Providers:Payment:AttemptTimeoutSeconds"] = attemptTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Providers:Payment:TotalTimeoutSeconds"] = totalTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(TimeProvider.System);
        services.AddFulfillmentHubPaymentProvider();
        services.AddHttpClient<IPaymentGatewayClient, SimulatedPaymentGatewayClient>()
            .ConfigurePrimaryHttpMessageHandler(() => transport);

        return services.BuildServiceProvider();
    }
}
