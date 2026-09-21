using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Deliveries;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Application.Payments;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Chaos;

/// <summary>
/// BL-150 / docs/TEST_STRATEGY.md T22 — convergence under provider failure injection. With the simulator answering
/// HTTP 500 to 30 % of every call (token, quote, create/get payment, create/get delivery), a batch of orders must still
/// end <c>Delivered</c>, with exactly one payment each and the stock accounted for once. What makes it converge is the
/// machinery under test: retries with backoff, the circuit breaker opening and closing, the outbox re-dispatching
/// <c>Retry</c> outcomes, and the Worker's sweeps (payment reconciliation, pending deliveries, delivery reconciliation),
/// which this test drives on the Worker's behalf between polls.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class ChaosConvergenceTests(ApiFixture api)
{
    private const int Orders = 8;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task OrdersConverge_ToDelivered_WhileTheProviderFails30PercentOfCalls()
    {
        var product = await CreateProductAsync(api, stock: Orders, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        using var chaos = api.Simulator.Chaos.Apply(new ChaosOptions { FailureRate = 0.3, LatencyJitterMs = 30 });
        var injectedFailures = 0;
        using var meters = CountSimulator500s(() => Interlocked.Increment(ref injectedFailures));

        var placed = new List<OrderDto>();
        for (var i = 0; i < Orders; i++)
        {
            // The checkout quote itself may hit a 500: the estimated fee fallback keeps the order flowing (D-51).
            var response = await PlaceOrderAsync(client, OrderBody((product.Id.Value, 1)), Guid.NewGuid().ToString());
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            placed.Add((await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!);
        }

        var deadline = DateTimeOffset.UtcNow + Budget;
        Dictionary<Guid, string> statuses;
        do
        {
            await RunWorkerSweepsAsync();
            await Task.Delay(500, TestContext.Current.CancellationToken);
            statuses = await StatusesAsync(placed.Select(o => o.Id));
        }
        while (statuses.Values.Any(s => s != nameof(OrderStatus.Delivered)) && DateTimeOffset.UtcNow < deadline);

        statuses.Values.ShouldAllBe(s => s == nameof(OrderStatus.Delivered), "orders that did not converge: " + string.Join(", ", statuses.Where(kv => kv.Value != "Delivered").Select(kv => $"{kv.Key}={kv.Value}")));

        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var orderIds = placed.Select(o => new OrderId(o.Id)).ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => orderIds.Contains(p.OrderId)).ToListAsync(TestContext.Current.CancellationToken);
        payments.Count.ShouldBe(Orders, "exactly one payment per order despite retries and redeliveries");
        payments.ShouldAllBe(p => p.Status == PaymentStatus.Paid);
        (await GetStockAsync(api, product.Id)).ShouldBe(0, "each order reserved its unit exactly once");
        Volatile.Read(ref injectedFailures).ShouldBeGreaterThan(0, "the simulator never returned a 500 — the scenario did not exercise the retry paths");
    }

    /// <summary>Counts HTTP 500 responses of the in-process simulator (ASP.NET Core's own request-duration metric).</summary>
    private static MeterListener CountSimulator500s(Action onFailure)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == "Microsoft.AspNetCore.Hosting" && instrument.Name == "http.server.request.duration")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "http.response.status_code" && tag.Value is int status && status == 500)
                {
                    onFailure();
                }
            }
        });
        listener.Start();
        return listener;
    }

    /// <summary>What the Worker's periodic jobs do (the test host only runs the outbox publisher).</summary>
    private async Task RunWorkerSweepsAsync()
    {
        using var scope = api.Services.CreateScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<ReconcilePaymentsHandler>().HandleAsync(pendingFor: TimeSpan.FromSeconds(1), batchSize: 100, TestContext.Current.CancellationToken);
        await services.GetRequiredService<RequestPendingDeliveriesHandler>().HandleAsync(100, TestContext.Current.CancellationToken);
        await services.GetRequiredService<ReconcileDeliveriesHandler>().HandleAsync(quietFor: TimeSpan.FromSeconds(1), batchSize: 100, TestContext.Current.CancellationToken);
    }

    private async Task<Dictionary<Guid, string>> StatusesAsync(IEnumerable<Guid> ids)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var orderIds = ids.Select(id => new OrderId(id)).ToList();
        return await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id.Value, o => o.Status.ToString(), TestContext.Current.CancellationToken);
    }
}
