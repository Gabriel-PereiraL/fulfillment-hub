using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FulfillmentHub.IntegrationTests.Fixtures;

namespace FulfillmentHub.IntegrationTests.Payments;

/// <summary>
/// The simulator must behave like the contract in docs/INTEGRATIONS.md §3: bearer auth, mandatory idempotency key with
/// replay/conflict semantics, snake_case payloads. The API's client is written against that contract.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class PaymentSimulatorContractTests(ApiFixture api)
{
    private const string PaymentsPath = "/payments/v1/payments";

    [Fact]
    public async Task CreatePayment_WithSameIdempotencyKey_ReplaysTheSamePayment()
    {
        using var client = CreateProviderClient();
        var key = Guid.NewGuid().ToString();
        var body = new { amount = 1990, currency = "BRL", order_reference = "order-1", customer_reference = "customer-1", capture = true };

        var first = await client.SendAsync(Request(key, body), TestContext.Current.CancellationToken);
        var replay = await client.SendAsync(Request(key, body), TestContext.Current.CancellationToken);
        var conflict = await client.SendAsync(Request(key, body with { amount = 2000 }), TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        replay.StatusCode.ShouldBe(HttpStatusCode.OK);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var created = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var replayed = await replay.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        replayed.GetProperty("id").GetString().ShouldBe(created.GetProperty("id").GetString());
        created.GetProperty("status").GetString().ShouldBe("pending");
        created.GetProperty("order_reference").GetString().ShouldBe("order-1");
    }

    [Fact]
    public async Task CreatePayment_WithoutIdempotencyKey_IsRejected()
    {
        using var client = CreateProviderClient();

        var response = await client.PostAsJsonAsync(PaymentsPath, new { amount = 1000, currency = "BRL", order_reference = "o", customer_reference = "c" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        error.GetProperty("code").GetString().ShouldBe("invalid_request");
    }

    [Fact]
    public async Task Requests_WithoutValidApiKey_Get401()
    {
        using var anonymous = api.Simulator.CreateClient();
        using var wrongKey = api.Simulator.CreateClient();
        wrongKey.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-key");

        (await anonymous.GetAsync($"{PaymentsPath}/pay_x", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrongKey.GetAsync($"{PaymentsPath}/pay_x", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPayment_Unknown_Returns404WithProviderErrorShape()
    {
        using var client = CreateProviderClient();

        var response = await client.GetAsync($"{PaymentsPath}/pay_missing", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        error.GetProperty("code").GetString().ShouldBe("not_found");
        error.GetProperty("kind").GetString().ShouldBe("error");
    }

    private HttpClient CreateProviderClient()
    {
        var client = api.Simulator.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ProviderSimulatorFactory.ApiKey);
        return client;
    }

    private static HttpRequestMessage Request(string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, PaymentsPath) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }
}
