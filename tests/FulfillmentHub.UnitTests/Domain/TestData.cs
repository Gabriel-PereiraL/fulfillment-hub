using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;

namespace FulfillmentHub.UnitTests.Domain;

/// <summary>Small factories for valid aggregates; tests then push them into the situation under test.</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public static Address Address() =>
        FulfillmentHub.Domain.Common.Address.Create("Rua das Flores", "123", "Apto 4", "Centro", "Recife", "PE", "50000-000", latitude: -8.05, longitude: -34.9);

    public static Product Product(string sku = "SKU-1", decimal price = 10m, int stock = 10) =>
        FulfillmentHub.Domain.Catalog.Product.Create(sku, $"Product {sku}", Money.Of(price), stock, Now);

    public static Customer Customer(bool active = true)
    {
        var customer = FulfillmentHub.Domain.Customers.Customer.Register(
            UserId.New(), "Ana Souza", EmailAddress.Of("ana@example.com"), PhoneNumber.Of("+55 81 99999-0000"), Now);

        if (!active)
        {
            customer.Deactivate(Now);
        }

        return customer;
    }

    public static Order Order(params (Product Product, int Quantity)[] lines)
    {
        var orderLines = lines.Length == 0
            ? [new OrderLine(Product(), 2)]
            : lines.Select(l => new OrderLine(l.Product, l.Quantity)).ToList();

        return FulfillmentHub.Domain.Orders.Order.Place(Customer(), Address(), orderLines, idempotencyKey: "idem-1", Now);
    }

    /// <summary>Drives an order to the requested status through the valid path.</summary>
    public static Order OrderIn(OrderStatus status)
    {
        var order = Order();
        var paymentId = PaymentId.New();

        if (status >= OrderStatus.AwaitingPayment && status != OrderStatus.Cancelled)
        {
            order.MarkAwaitingPayment(paymentId, Now);
        }

        if (status >= OrderStatus.Paid && status != OrderStatus.Cancelled)
        {
            order.MarkAsPaid(paymentId, Now);
        }

        if (status >= OrderStatus.DeliveryRequested && status != OrderStatus.Cancelled)
        {
            order.MarkDeliveryRequested(DeliveryId.New(), Money.Of(7.5m), Now);
        }

        if (status >= OrderStatus.InDelivery && status != OrderStatus.Cancelled)
        {
            order.MarkInDelivery(Now);
        }

        if (status == OrderStatus.Delivered)
        {
            order.MarkDelivered(Now);
        }

        if (status == OrderStatus.Cancelled)
        {
            order.Cancel(OrderCancellationReason.CustomerRequest, Now);
        }

        order.ClearDomainEvents();
        return order;
    }

    public static Payment Payment() => FulfillmentHub.Domain.Payments.Payment.Create(OrderId.New(), Money.Of(25m), "simulated-psp", Now);

    public static DeliveryQuote Quote(OrderId? orderId = null, DateTimeOffset? expiresAt = null) =>
        DeliveryQuote.Create(
            orderId ?? OrderId.New(),
            "uber-like-simulator",
            "dqt_" + Guid.NewGuid().ToString("N")[..8],
            Money.Of(12.9m),
            Now.AddMinutes(45),
            durationMinutes: 33,
            pickupDurationMinutes: 12,
            expiresAt ?? Now.AddMinutes(15),
            Now);

    /// <summary>A delivery already accepted by the provider (status Pending).</summary>
    public static Delivery Delivery()
    {
        var delivery = FulfillmentHub.Domain.Deliveries.Delivery.Request(Quote(), attempt: 1, Now);
        delivery.ConfirmCreated("del_123", "https://tracking.example/del_123", Money.Of(12.9m), Now);
        return delivery;
    }

    public static User User(params Role[] roles) =>
        FulfillmentHub.Domain.Identity.User.Create(EmailAddress.Of("user@example.com"), "hash", roles.Length == 0 ? [Role.Customer] : roles, Now);
}
