using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FulfillmentHub.E2ETests;

/// <summary>
/// The running stack under test, described only by environment variables:
/// <c>FH_E2E_API_URL</c> (required to run; tests are skipped without it), <c>FH_E2E_CUSTOMER_EMAIL</c>
/// (default <c>customer@fulfillmenthub.local</c>, the development seed) and <c>FH_E2E_CUSTOMER_PASSWORD</c>.
/// </summary>
public sealed class RunningSystem : IAsyncLifetime
{
    public static readonly string? ApiUrl = Environment.GetEnvironmentVariable("FH_E2E_API_URL");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient? _client;

    public HttpClient Client => _client ?? throw new InvalidOperationException("The stack is not initialised (FH_E2E_API_URL not set).");

    public static void SkipUnlessConfigured() =>
        Assert.SkipWhen(string.IsNullOrWhiteSpace(ApiUrl), "FH_E2E_API_URL is not set: end-to-end tests need a running stack (scripts/run-e2e.sh)");

    public async ValueTask InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiUrl))
        {
            return;
        }

        var email = Environment.GetEnvironmentVariable("FH_E2E_CUSTOMER_EMAIL") ?? "customer@fulfillmenthub.local";
        var password = Environment.GetEnvironmentVariable("FH_E2E_CUSTOMER_PASSWORD")
            ?? throw new InvalidOperationException("FH_E2E_CUSTOMER_PASSWORD is required when FH_E2E_API_URL is set.");

        var client = new HttpClient { BaseAddress = new Uri(ApiUrl), Timeout = TimeSpan.FromSeconds(30) };
        var ready = await client.GetAsync("/health/ready");
        ready.EnsureSuccessStatusCode();

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var token = await login.Content.ReadFromJsonAsync<LoginResult>(Json) ?? throw new InvalidOperationException("Empty login response.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(token.TokenType, token.AccessToken);
        _client = client;
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    public async Task<Product> ProductAsync(string sku)
    {
        var products = await Client.GetFromJsonAsync<List<Product>>("/api/v1/products", Json) ?? [];
        return products.SingleOrDefault(p => p.Sku == sku)
            ?? throw new InvalidOperationException($"Product '{sku}' is not seeded (dotnet run --project src/FulfillmentHub.Api -- seed).");
    }

    public async Task<Order> PlaceOrderAsync(Guid productId, string postalCode)
    {
        var body = new
        {
            items = new[] { new { productId, quantity = 1 } },
            deliveryAddress = new
            {
                street = "Rua das Flores",
                number = "123",
                district = "Centro",
                city = "Recife",
                state = "PE",
                postalCode,
                country = "BR",
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", $"e2e-{Guid.NewGuid():N}");
        using var response = await Client.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.ShouldBeTrue($"POST /orders → {(int)response.StatusCode}: {payload}");
        return JsonSerializer.Deserialize<Order>(payload, Json)!;
    }

    /// <summary>Polls the order until <paramref name="until"/> holds or the budget runs out; returns the last state.</summary>
    public async Task<Order> WaitAsync(Guid orderId, Func<Order, bool> until, TimeSpan budget)
    {
        var deadline = DateTimeOffset.UtcNow + budget;
        Order? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await Client.GetFromJsonAsync<Order>($"/api/v1/orders/{orderId}", Json);
            if (last is not null && until(last))
            {
                return last;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"Order {orderId} did not reach the expected state within {budget}; last: {last?.Status}/{last?.CancellationReason}");
    }

    public sealed record LoginResult(string AccessToken, string TokenType);

    public sealed record Product(Guid Id, string Sku, int StockQuantity);

    public sealed record Order(Guid Id, string Status, string? CancellationReason, Guid? PaymentId, Guid? DeliveryId);
}
