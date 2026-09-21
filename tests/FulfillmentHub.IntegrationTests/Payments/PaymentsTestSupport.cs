using System.Net.Http.Json;
using System.Text;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.Infrastructure.Webhooks;
using FulfillmentHub.IntegrationTests.Fixtures;
using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.IntegrationTests.Payments;

internal static class PaymentsTestSupport
{
    public const string WebhookPath = "/api/v1/webhooks/payments";
    public const string ProviderName = "simulated-psp";

    /// <summary>Sandbox amounts of the simulator (see PaymentSimulatorStore.ScenarioFromAmount).</summary>
    public const decimal ApprovedPrice = 19.90m;
    public const decimal DeclinedPrice = 9.99m;
    public const decimal SilentlyApprovedPrice = 9.98m;

    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Polls the public API until the order reaches <paramref name="status"/> — or has already moved past it along the
    /// happy path (`Created → … → Delivered`): with webhooks flowing in-process a transient status such as `Paid` can be
    /// left behind between two polls on a fast machine (seen once on the CI runner). `Cancelled` is never "past" anything.
    /// Fails after a generous timeout.
    /// </summary>
    public static async Task<OrderDto> WaitForOrderStatusAsync(HttpClient client, Guid orderId, string status)
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        var target = Enum.Parse<OrderStatus>(status);
        OrderDto? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{orderId}", TestContext.Current.CancellationToken);
            if (last is not null
                && Enum.TryParse<OrderStatus>(last.Status, out var current)
                && (current == target || (target != OrderStatus.Cancelled && current != OrderStatus.Cancelled && current > target)))
            {
                return last;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Order {orderId} did not reach status '{status}' within {PollTimeout}; last status: {last?.Status}");
    }

    public static async Task<Payment> GetPaymentAsync(ApiFixture api, Guid paymentId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Payments.AsNoTracking().Include(p => p.Attempts).SingleAsync(p => p.Id == new PaymentId(paymentId));
    }

    /// <summary>The payment created for an order; the order only links to it once the provider accepted it.</summary>
    /// <summary>Polls until the payment reaches <paramref name="status"/> (outbox handlers and provider settlement are asynchronous).</summary>
    public static async Task<Payment> WaitForPaymentStatusAsync(ApiFixture api, Guid paymentId, PaymentStatus status)
    {
        var deadline = DateTimeOffset.UtcNow + PollTimeout;
        Payment? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await GetPaymentAsync(api, paymentId);
            if (last.Status == status)
            {
                return last;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Payment {paymentId} did not reach status '{status}' within {PollTimeout}; last status: {last?.Status}");
    }

    public static async Task<Payment> GetPaymentForOrderAsync(ApiFixture api, Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Payments.AsNoTracking().Include(p => p.Attempts).SingleAsync(p => p.OrderId == new OrderId(orderId));
    }

    public static async Task<Order> GetOrderAggregateAsync(ApiFixture api, Guid orderId)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.Orders.AsNoTracking().Include(o => o.StatusHistory).SingleAsync(o => o.Id == new OrderId(orderId));
    }

    public static async Task<List<WebhookEvent>> GetWebhookEventsAsync(ApiFixture api, string providerEventId, string provider = ProviderName)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        return await db.WebhookEvents.AsNoTracking()
            .Where(e => e.Provider == provider && e.ProviderEventId == providerEventId)
            .ToListAsync();
    }

    /// <summary>Builds a webhook exactly as the simulator would send it (same JSON shape, same HMAC scheme).</summary>
    public static HttpRequestMessage SignedWebhook(
        string body,
        string signingKey = ProviderSimulatorFactory.WebhookSigningKey,
        DateTimeOffset? timestamp = null,
        bool includeSignature = true,
        string? signedBody = null,
        string path = WebhookPath,
        string signatureHeader = WebhookDispatcher.SignatureHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        if (includeSignature)
        {
            request.Headers.Add(signatureHeader, WebhookDispatcher.Sign(signingKey, signedBody ?? body));
        }

        request.Headers.Add(WebhookDispatcher.TimestampHeader,
            (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        return request;
    }

    public static string StatusChangedEvent(string eventId, string providerPaymentId, string status, string? failureCode = null)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var failure = failureCode is null ? "null" : $"\"{failureCode}\"";

        return $"{{\"id\":\"{eventId}\",\"type\":\"payment.status_changed\",\"created_at\":\"{now}\","
            + $"\"data\":{{\"payment_id\":\"{providerPaymentId}\",\"status\":\"{status}\",\"failure_code\":{failure},"
            + $"\"order_reference\":\"ignored\",\"occurred_at\":\"{now}\"}}}}";
    }
}
