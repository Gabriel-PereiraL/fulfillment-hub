using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.IntegrationTests.Fixtures;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;
using static FulfillmentHub.IntegrationTests.Payments.PaymentsTestSupport;

namespace FulfillmentHub.IntegrationTests.Outbox;

/// <summary>
/// BL-127 / docs/OBSERVABILITY.md §2: one order produces one end-to-end trace. The `traceparent` accepted by the API is
/// stored on the outbox message, the Worker-side handler continues it as a consumer span, and the provider call made
/// from that handler still carries the same trace id — across the HTTP hop into the simulator.
/// </summary>
[Collection(ApiTests.Name)]
public sealed class TraceContinuityTests(ApiFixture api)
{
    [Fact]
    public async Task PlaceOrder_OutboxHandler_AndProviderCall_ShareTheRequestTraceId()
    {
        var product = await CreateProductAsync(api, stock: 5, price: ApprovedPrice);
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        var traceId = ActivityTraceId.CreateRandom();

        // Pause the background publisher and drain what earlier tests of this collection left in the outbox *before*
        // the listener exists: RunOnceAsync processes the whole pending batch, so a leftover OrderPlaced from another
        // order would otherwise produce an "Outbox OrderPlaced" span on a foreign trace and fail the assertion below.
        using var pause = api.Outbox.Pause();
        await api.Outbox.RunOnceAsync();

        var captured = new List<(string Source, string Name, ActivityTraceId TraceId)>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "FulfillmentHub" or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (captured)
                {
                    captured.Add((activity.Source.Name, activity.DisplayName, activity.TraceId));
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/orders") { Content = JsonContent.Create(OrderBody((product.Id.Value, 1))) };
        request.Headers.Add(IdempotencyHeader, Guid.NewGuid().ToString());
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = (await response.Content.ReadFromJsonAsync<OrderDto>(TestContext.Current.CancellationToken))!;

        // The Worker's publisher pass: OrderPlaced → CreatePayment → provider HTTP call (in-process simulator).
        await api.Outbox.RunOnceAsync();

        List<(string Source, string Name, ActivityTraceId TraceId)> snapshot;
        lock (captured)
        {
            snapshot = [.. captured];
        }

        var expected = new[] { "PlaceOrder", "Outbox OrderPlaced", "CreatePayment", "Provider CreatePayment" };
        foreach (var name in expected)
        {
            snapshot.Where(a => a.Name == name).ShouldNotBeEmpty($"span '{name}' was not observed");
            snapshot.Where(a => a.Name == name).ShouldAllBe(a => a.TraceId == traceId, $"span '{name}' left the order's trace");
        }

        // The simulator's server span for the payment creation is the far end of the same trace (W3C propagation over HTTP).
        var simulatorSpans = snapshot.Where(a => a.Source == "Microsoft.AspNetCore" && a.Name.Contains("/payments/v1/payments", StringComparison.Ordinal)).ToList();
        simulatorSpans.ShouldNotBeEmpty("the simulator's server span was not observed; seen: " + string.Join(" | ", snapshot.Select(a => a.Source + ":" + a.Name).Distinct()));
        simulatorSpans.ShouldAllBe(a => a.TraceId == traceId, "mismatched: " + string.Join(" | ", simulatorSpans.Where(a => a.TraceId != traceId).Select(a => a.Name + "@" + a.TraceId)) + " expected " + traceId);

        (await client.GetFromJsonAsync<OrderDto>($"/api/v1/orders/{placed.Id}", TestContext.Current.CancellationToken))!
            .Status.ShouldBe("AwaitingPayment", "the payment was created by the handler that continued the trace");
    }
}
