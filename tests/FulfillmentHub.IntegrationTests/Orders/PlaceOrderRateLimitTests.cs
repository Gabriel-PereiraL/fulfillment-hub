using System.Net;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.IntegrationTests.Fixtures;
using static FulfillmentHub.IntegrationTests.Orders.OrdersTestSupport;

namespace FulfillmentHub.IntegrationTests.Orders;

/// <summary>`POST /orders` budget per authenticated user (D-84, BL-103): 60/min by default, sliding window.</summary>
[Collection(ApiTests.Name)]
public sealed class PlaceOrderRateLimitTests(ApiFixture api)
{
    private const int OrdersPerMinute = 60;

    [Fact]
    public async Task PlaceOrder_BeyondTheUserBudget_Returns429_WithoutAffectingOtherUsers()
    {
        using var client = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);

        // Requests are counted before the handler runs, so an invalid body (400) is enough to spend the budget
        // without creating orders.
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt <= OrdersPerMinute; attempt++)
        {
            statuses.Add((await PlaceOrderAsync(client, new { items = Array.Empty<object>() }, Guid.NewGuid().ToString())).StatusCode);
        }

        statuses.Take(OrdersPerMinute).ShouldAllBe(status => status == HttpStatusCode.BadRequest);
        statuses[OrdersPerMinute].ShouldBe(HttpStatusCode.TooManyRequests);

        // The partition is the user, not the process: another customer still has a full budget.
        using var otherClient = await api.CreateAuthenticatedClientAsync([Role.Customer], withCustomerProfile: true);
        (await PlaceOrderAsync(otherClient, new { items = Array.Empty<object>() }, Guid.NewGuid().ToString()))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
