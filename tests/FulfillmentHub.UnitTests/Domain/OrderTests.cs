using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Orders.Events;
using FulfillmentHub.Domain.Payments;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Place_SnapshotsPricesAndComputesTotals()
    {
        var product = TestData.Product("SKU-A", price: 19.9m, stock: 5);
        var other = TestData.Product("SKU-B", price: 5.05m, stock: 5);

        var order = TestData.Order((product, 2), (other, 1));

        order.Status.ShouldBe(OrderStatus.Created);
        order.Items.Count.ShouldBe(2);
        order.Items[0].UnitPrice.ShouldBe(Money.Of(19.9m));
        order.Items[0].LineTotal.ShouldBe(Money.Of(39.8m));
        order.Subtotal.ShouldBe(Money.Of(44.85m));
        order.Total.ShouldBe(order.Subtotal);
        order.DeliveryFee.ShouldBeNull();
        order.StatusHistory.ShouldHaveSingleItem().To.ShouldBe(OrderStatus.Created);
        order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderPlaced>().Total.ShouldBe(order.Total);

        // Changing the product afterwards must not affect the order's snapshot.
        product.ChangePrice(Money.Of(99m), Now);
        order.Items[0].UnitPrice.ShouldBe(Money.Of(19.9m));
    }

    [Fact]
    public void Place_WithoutItems_Throws()
    {
        var act = () => Order.Place(TestData.Customer(), TestData.Address(), [], null, Now);

        act.ShouldThrow<DomainException>().Message.ShouldContain("between 1 and");
    }

    [Fact]
    public void Place_WithMoreThanMaxItems_Throws()
    {
        var lines = Enumerable.Range(0, Order.MaxItems + 1).Select(i => new OrderLine(TestData.Product($"SKU-{i}"), 1)).ToList();

        var act = () => Order.Place(TestData.Customer(), TestData.Address(), lines, null, Now);

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void Place_WithRepeatedProduct_Throws()
    {
        var product = TestData.Product();

        var act = () => TestData.Order((product, 1), (product, 2));

        act.ShouldThrow<DomainException>().Message.ShouldContain("more than once");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(OrderItem.MaxQuantity + 1)]
    public void Place_WithInvalidQuantity_Throws(int quantity)
    {
        var act = () => TestData.Order((TestData.Product(), quantity));

        act.ShouldThrow<DomainException>();
    }

    [Fact]
    public void Place_WithInactiveProduct_Throws()
    {
        var product = TestData.Product();
        product.Deactivate(Now);

        var act = () => TestData.Order((product, 1));

        act.ShouldThrow<DomainException>().Message.ShouldContain("inactive");
    }

    [Fact]
    public void Place_WithInactiveCustomer_Throws()
    {
        var act = () => Order.Place(TestData.Customer(active: false), TestData.Address(), [new OrderLine(TestData.Product(), 1)], null, Now);

        act.ShouldThrow<DomainException>().Message.ShouldContain("inactive");
    }

    [Fact]
    public void HappyPath_WalksEveryStatusAndRaisesEvents()
    {
        var order = TestData.Order();
        var paymentId = PaymentId.New();
        var deliveryId = DeliveryId.New();

        order.MarkAwaitingPayment(paymentId, Now.AddMinutes(1));
        order.MarkAsPaid(paymentId, Now.AddMinutes(2));
        order.MarkDeliveryRequested(deliveryId, Money.Of(8m), Now.AddMinutes(3));
        order.MarkInDelivery(Now.AddMinutes(4));
        order.MarkDelivered(Now.AddMinutes(5));

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.IsFinal.ShouldBeTrue();
        order.PaymentId.ShouldBe(paymentId);
        order.DeliveryId.ShouldBe(deliveryId);
        order.DeliveryFee.ShouldBe(Money.Of(8m));
        order.Total.ShouldBe(order.Subtotal + Money.Of(8m));
        order.StatusHistory.Select(h => h.To).ShouldBe(
        [
            OrderStatus.Created,
            OrderStatus.AwaitingPayment,
            OrderStatus.Paid,
            OrderStatus.DeliveryRequested,
            OrderStatus.InDelivery,
            OrderStatus.Delivered,
        ]);
        order.DomainEvents.Select(e => e.GetType()).ShouldBe(
            [typeof(OrderPlaced), typeof(OrderPaid), typeof(OrderDeliveryRequested), typeof(OrderDelivered)]);
    }

    [Theory]
    [InlineData(OrderStatus.Created, OrderStatus.Paid)]
    [InlineData(OrderStatus.Created, OrderStatus.DeliveryRequested)]
    [InlineData(OrderStatus.AwaitingPayment, OrderStatus.InDelivery)]
    [InlineData(OrderStatus.Paid, OrderStatus.InDelivery)]
    [InlineData(OrderStatus.Paid, OrderStatus.Delivered)]
    [InlineData(OrderStatus.DeliveryRequested, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.InDelivery)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Paid)]
    [InlineData(OrderStatus.Delivered, OrderStatus.AwaitingPayment)]
    public void InvalidTransitions_Throw(OrderStatus from, OrderStatus attempted)
    {
        var order = TestData.OrderIn(from);

        Action act = attempted switch
        {
            OrderStatus.AwaitingPayment => () => order.MarkAwaitingPayment(PaymentId.New(), Now),
            OrderStatus.Paid => () => order.MarkAsPaid(PaymentId.New(), Now),
            OrderStatus.DeliveryRequested => () => order.MarkDeliveryRequested(DeliveryId.New(), Money.Of(1m), Now),
            OrderStatus.InDelivery => () => order.MarkInDelivery(Now),
            OrderStatus.Delivered => () => order.MarkDelivered(Now),
            _ => throw new InvalidOperationException(),
        };

        var exception = act.ShouldThrow<InvalidStateTransitionException>();
        exception.From.ShouldBe(from.ToString());
        exception.To.ShouldBe(attempted.ToString());
        order.Status.ShouldBe(from, "a rejected transition must not change state");
    }

    [Fact]
    public void RepeatedTransitionToSameStatus_IsIdempotent()
    {
        var order = TestData.OrderIn(OrderStatus.InDelivery);

        order.MarkDelivered(Now);
        order.MarkDelivered(Now.AddMinutes(1));

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.StatusHistory.Count(h => h.To == OrderStatus.Delivered).ShouldBe(1);
        order.DomainEvents.OfType<OrderDelivered>().Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(OrderStatus.Created, OrderCancellationReason.CustomerRequest, true)]
    [InlineData(OrderStatus.AwaitingPayment, OrderCancellationReason.CustomerRequest, true)]
    [InlineData(OrderStatus.Paid, OrderCancellationReason.CustomerRequest, true)]
    [InlineData(OrderStatus.DeliveryRequested, OrderCancellationReason.CustomerRequest, true)]
    [InlineData(OrderStatus.InDelivery, OrderCancellationReason.CustomerRequest, false)]
    [InlineData(OrderStatus.InDelivery, OrderCancellationReason.DeliveryFailed, true)]
    [InlineData(OrderStatus.InDelivery, OrderCancellationReason.OperatorAction, true)]
    [InlineData(OrderStatus.Paid, OrderCancellationReason.PaymentFailed, false)]
    [InlineData(OrderStatus.AwaitingPayment, OrderCancellationReason.PaymentFailed, true)]
    [InlineData(OrderStatus.AwaitingPayment, OrderCancellationReason.StockUnavailable, false)]
    [InlineData(OrderStatus.Created, OrderCancellationReason.StockUnavailable, true)]
    [InlineData(OrderStatus.Delivered, OrderCancellationReason.OperatorAction, false)]
    public void Cancel_IsAllowedOnlyForCoherentStatusAndReason(OrderStatus status, OrderCancellationReason reason, bool allowed)
    {
        var order = TestData.OrderIn(status);

        var act = () => order.Cancel(reason, Now);

        if (allowed)
        {
            act.ShouldNotThrow();
            order.Status.ShouldBe(OrderStatus.Cancelled);
            order.CancellationReason.ShouldBe(reason);
            var cancelled = order.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<OrderCancelled>();
            cancelled.PreviousStatus.ShouldBe(status);
            cancelled.Reason.ShouldBe(reason);
        }
        else
        {
            act.ShouldThrow<DomainException>();
            order.Status.ShouldBe(status);
        }
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsNoOp()
    {
        var order = TestData.OrderIn(OrderStatus.Cancelled);

        order.Cancel(OrderCancellationReason.OperatorAction, Now);

        order.CancellationReason.ShouldBe(OrderCancellationReason.CustomerRequest);
        order.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void MarkDeliveryRequested_WithNegativeFee_Throws()
    {
        var order = TestData.OrderIn(OrderStatus.Paid);

        var act = () => order.MarkDeliveryRequested(DeliveryId.New(), Money.Of(-1m), Now);

        act.ShouldThrow<DomainException>();
        order.Status.ShouldBe(OrderStatus.Paid);
    }
}
