using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;
using FulfillmentHub.Infrastructure.Persistence;
using FulfillmentHub.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FulfillmentHub.IntegrationTests.Persistence;

/// <summary>Proves the EF Core mapping round-trips every aggregate, including complex types and DB-generated values.</summary>
[Collection(ApiTests.Name)]
public sealed class DomainPersistenceTests(ApiFixture api)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Order_RoundTrips_WithItemsHistoryAddressAndGeneratedNumber()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = NewProduct(price: 19.9m, stock: 10);
        var customer = NewCustomer();
        var order = Order.Place(customer, NewAddress(), [new OrderLine(product, 3)], "idem-" + Guid.NewGuid().ToString("N"), Now);
        order.MarkAwaitingPayment(PaymentId.New(), Now.AddMinutes(1));

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            db.Products.Add(product);
            db.Customers.Add(customer);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            var loaded = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);

            loaded.Number.ShouldBeGreaterThanOrEqualTo(1000, "the number comes from the database sequence");
            loaded.Status.ShouldBe(OrderStatus.AwaitingPayment);
            loaded.Subtotal.ShouldBe(Money.Of(59.7m));
            loaded.Total.ShouldBe(Money.Of(59.7m));
            loaded.DeliveryFee.ShouldBeNull();
            loaded.DeliveryAddress.ShouldBe(order.DeliveryAddress);
            loaded.Items.ShouldHaveSingleItem().LineTotal.ShouldBe(Money.Of(59.7m));
            loaded.StatusHistory.Select(h => h.To).ShouldBe([OrderStatus.Created, OrderStatus.AwaitingPayment]);
            loaded.IdempotencyKey.ShouldBe(order.IdempotencyKey);
        }
    }

    [Fact]
    public async Task Order_DeliveryFee_IsPersistedAsOptionalComplexType()
    {
        var ct = TestContext.Current.CancellationToken;
        var order = Order.Place(NewCustomer(), NewAddress(), [new OrderLine(NewProduct(), 1)], null, Now);
        var paymentId = PaymentId.New();
        order.MarkAwaitingPayment(paymentId, Now);
        order.MarkAsPaid(paymentId, Now);
        order.MarkDeliveryRequested(DeliveryId.New(), Money.Of(7.25m), Now);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            var loaded = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id, ct);

            loaded.DeliveryFee.ShouldBe(Money.Of(7.25m));
            loaded.Total.ShouldBe(order.Subtotal + Money.Of(7.25m));
        }
    }

    [Fact]
    public async Task Customer_Delivery_Payment_And_User_RoundTrip()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = NewCustomer();
        customer.AddAddress("Home", NewAddress(), Now);
        var quote = DeliveryQuote.Create(OrderId.New(), "uber-like-simulator", "dqt_" + Guid.NewGuid().ToString("N"), Money.Of(12.9m), Now.AddMinutes(40), 30, 10, Now.AddMinutes(15), Now);
        var delivery = Delivery.Request(quote, 1, Now);
        delivery.ConfirmCreated("del_" + Guid.NewGuid().ToString("N"), "https://track/x", Money.Of(12.9m), Now);
        delivery.ApplyProviderEvent("evt-1", "pickup", DeliveryStatus.Pickup, Now.AddMinutes(1),
            CourierInfo.Create("Maria", PhoneNumber.Of("+5581999990000"), "bike", -8.0, -34.9), Now.AddMinutes(1));
        var payment = Payment.Create(OrderId.New(), Money.Of(59.7m), "simulated-psp", Now);
        var attempt = payment.StartAttempt(Now);
        payment.CompleteAttempt(attempt.Id, PaymentAttemptOutcome.Succeeded, "pay_" + Guid.NewGuid().ToString("N"), null, Now);
        var user = User.Create(EmailAddress.Of($"{Guid.NewGuid():N}@example.com"), "hash", [Role.Operator, Role.Admin], Now);

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
            db.AddRange(customer, quote, delivery, payment, user);
            await db.SaveChangesAsync(ct);
        }

        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();

            var loadedCustomer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customer.Id, ct);
            loadedCustomer.Addresses.ShouldHaveSingleItem().IsDefault.ShouldBeTrue();
            loadedCustomer.Email.ShouldBe(customer.Email);
            loadedCustomer.Phone.ShouldBe(customer.Phone);

            var loadedDelivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery.Id, ct);
            loadedDelivery.Status.ShouldBe(DeliveryStatus.Pickup);
            loadedDelivery.Courier.ShouldNotBeNull().PhoneMasked.ShouldBe("+55*******0000");
            loadedDelivery.Events.ShouldHaveSingleItem().Disposition.ShouldBe(DeliveryEventDisposition.Applied);

            var loadedPayment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == payment.Id, ct);
            loadedPayment.Attempts.ShouldHaveSingleItem().Outcome.ShouldBe(PaymentAttemptOutcome.Succeeded);
            loadedPayment.ProviderPaymentId.ShouldBe(payment.ProviderPaymentId);

            var loadedUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id, ct);
            loadedUser.Roles.ShouldBe([Role.Operator, Role.Admin]);
        }
    }

    private static Product NewProduct(decimal price = 10m, int stock = 10) =>
        Product.Create("SKU-" + Guid.NewGuid().ToString("N")[..12], "Test product", Money.Of(price), stock, Now);

    private static Customer NewCustomer() =>
        Customer.Register(UserId.New(), "Ana", EmailAddress.Of($"{Guid.NewGuid():N}@example.com"), PhoneNumber.Of("+5581999990000"), Now);

    private static Address NewAddress() =>
        Address.Create("Rua das Flores", "123", "Apto 4", "Centro", "Recife", "PE", "50000-000", latitude: -8.05, longitude: -34.9);
}
