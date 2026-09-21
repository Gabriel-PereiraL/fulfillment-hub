namespace FulfillmentHub.E2ETests;

/// <summary>
/// The three end-to-end flows of Phase 12 (docs/ROADMAP.md, docs/TEST_STRATEGY.md T23), observed only through the
/// public API of a running stack: API + Worker + provider simulator + PostgreSQL + SQS. Timings follow the simulator's
/// Development settings (payment settles in ~2 s; the courier advances every 3 s).
/// </summary>
public sealed class OrderFlowsTests(RunningSystem system) : IClassFixture<RunningSystem>
{
    private static readonly TimeSpan FlowBudget = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Order_IsPaidAndDelivered()
    {
        RunningSystem.SkipUnlessConfigured();
        var product = await system.ProductAsync("MUG-DOTNET");

        var placed = await system.PlaceOrderAsync(product.Id, postalCode: "50000-000");
        placed.Status.ShouldBe("Created");

        var delivered = await system.WaitAsync(placed.Id, o => o.Status == "Delivered", FlowBudget);
        delivered.PaymentId.ShouldNotBeNull();
        delivered.DeliveryId.ShouldNotBeNull();
        delivered.CancellationReason.ShouldBeNull();
    }

    [Fact]
    public async Task DeclinedPayment_CancelsTheOrder_AndReturnsStock()
    {
        RunningSystem.SkipUnlessConfigured();
        var product = await system.ProductAsync("SANDBOX-DECLINE"); // total ends in .99 → the simulator declines

        var placed = await system.PlaceOrderAsync(product.Id, postalCode: "50000-000");
        var cancelled = await system.WaitAsync(placed.Id, o => o.Status == "Cancelled", FlowBudget);

        cancelled.CancellationReason.ShouldBe("PaymentFailed");
        cancelled.DeliveryId.ShouldBeNull("nothing was sent to the delivery provider");
        (await system.ProductAsync("SANDBOX-DECLINE")).StockQuantity.ShouldBe(product.StockQuantity, "the reserved unit came back");
    }

    [Fact]
    public async Task ReturnedDelivery_CancelsTheOrder()
    {
        RunningSystem.SkipUnlessConfigured();
        var product = await system.ProductAsync("STICKER-PACK");

        // Sandbox rule of the delivery simulator: a dropoff postal code ending in 002 is picked up and then returned.
        var placed = await system.PlaceOrderAsync(product.Id, postalCode: "50000-002");
        var cancelled = await system.WaitAsync(placed.Id, o => o.Status == "Cancelled", FlowBudget);

        cancelled.CancellationReason.ShouldBe("DeliveryFailed");
        cancelled.PaymentId.ShouldNotBeNull("the order was paid before the delivery failed");
        cancelled.DeliveryId.ShouldNotBeNull();
    }
}
