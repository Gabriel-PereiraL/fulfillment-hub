using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class DeliveryTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Request_WithExpiredQuote_Throws()
    {
        var quote = TestData.Quote(expiresAt: Now.AddMinutes(10));

        var act = () => Delivery.Request(quote, attempt: 1, Now.AddMinutes(10));

        act.ShouldThrow<DomainException>().Message.ShouldContain("expired");
    }

    [Fact]
    public void Request_DerivesIdempotencyKeyFromOrderAndAttempt()
    {
        var quote = TestData.Quote();

        var first = Delivery.Request(quote, 1, Now);
        var retry = Delivery.Request(quote, 1, Now);
        var requote = Delivery.Request(quote, 2, Now);

        first.ProviderIdempotencyKey.ShouldBe(retry.ProviderIdempotencyKey);
        requote.ProviderIdempotencyKey.ShouldNotBe(first.ProviderIdempotencyKey);
        first.Status.ShouldBe(DeliveryStatus.Requested);
    }

    [Fact]
    public void ProviderEvents_InOrder_MoveStatusForward()
    {
        var delivery = TestData.Delivery();

        Apply(delivery, "evt-1", DeliveryStatus.Pickup, minutes: 1).ShouldBe(DeliveryEventDisposition.Applied);
        Apply(delivery, "evt-2", DeliveryStatus.PickupComplete, minutes: 2).ShouldBe(DeliveryEventDisposition.Applied);
        Apply(delivery, "evt-3", DeliveryStatus.Dropoff, minutes: 3).ShouldBe(DeliveryEventDisposition.Applied);
        Apply(delivery, "evt-4", DeliveryStatus.Delivered, minutes: 4).ShouldBe(DeliveryEventDisposition.Applied);

        delivery.Status.ShouldBe(DeliveryStatus.Delivered);
        delivery.IsFinal.ShouldBeTrue();
        delivery.Events.Count.ShouldBe(4);
        delivery.LastProviderEventAt.ShouldBe(Now.AddMinutes(4));
    }

    [Fact]
    public void DuplicateProviderEvent_IsRecordedButNotApplied()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-1", DeliveryStatus.Pickup, minutes: 1);

        var disposition = Apply(delivery, "evt-1", DeliveryStatus.Pickup, minutes: 1);

        disposition.ShouldBe(DeliveryEventDisposition.Duplicate);
        delivery.Events.Count.ShouldBe(2);
        delivery.Events[1].Disposition.ShouldBe(DeliveryEventDisposition.Duplicate);
    }

    [Fact]
    public void OutOfOrderEvent_DoesNotRegressStatus()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-delivered", DeliveryStatus.Delivered, minutes: 5);

        // The pickup event has a newer *receive* time but an older *occurrence* time: stale.
        var stale = Apply(delivery, "evt-pickup", DeliveryStatus.Pickup, minutes: 2);

        stale.ShouldBe(DeliveryEventDisposition.Stale);
        delivery.Status.ShouldBe(DeliveryStatus.Delivered);
    }

    [Fact]
    public void BackwardsStatusWithNewerTimestamp_IsOutOfOrder()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-1", DeliveryStatus.Dropoff, minutes: 3);

        var disposition = Apply(delivery, "evt-2", DeliveryStatus.Pickup, minutes: 4);

        disposition.ShouldBe(DeliveryEventDisposition.OutOfOrder);
        delivery.Status.ShouldBe(DeliveryStatus.Dropoff);
    }

    [Fact]
    public void FinalStatusFollowedByAnotherFinalStatus_IsConflict()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-1", DeliveryStatus.Delivered, minutes: 3);

        var disposition = Apply(delivery, "evt-2", DeliveryStatus.Returned, minutes: 4);

        disposition.ShouldBe(DeliveryEventDisposition.Conflict);
        delivery.Status.ShouldBe(DeliveryStatus.Delivered);
    }

    [Fact]
    public void CancelledEvent_IsAppliedFromAnyNonFinalStatus()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-1", DeliveryStatus.Dropoff, minutes: 3);

        Apply(delivery, "evt-2", DeliveryStatus.Cancelled, minutes: 4).ShouldBe(DeliveryEventDisposition.Applied);

        delivery.Status.ShouldBe(DeliveryStatus.Cancelled);
    }

    [Fact]
    public void Cancel_AfterPickup_Throws()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-1", DeliveryStatus.PickupComplete, minutes: 1);

        Should.Throw<InvalidStateTransitionException>(() => delivery.Cancel(Now));
        delivery.Status.ShouldBe(DeliveryStatus.PickupComplete);
    }

    [Fact]
    public void Cancel_BeforePickup_Succeeds()
    {
        var delivery = TestData.Delivery();
        Apply(delivery, "evt-1", DeliveryStatus.Pickup, minutes: 1);

        delivery.Cancel(Now);

        delivery.Status.ShouldBe(DeliveryStatus.Cancelled);
    }

    [Fact]
    public void ProviderEvent_BeforeProviderConfirmedCreation_Throws()
    {
        var delivery = Delivery.Request(TestData.Quote(), 1, Now);

        Should.Throw<InvalidStateTransitionException>(() => Apply(delivery, "evt-1", DeliveryStatus.Pickup, minutes: 1));
    }

    [Fact]
    public void CourierInfo_IsStoredMasked()
    {
        var delivery = TestData.Delivery();
        var courier = CourierInfo.Create("João", PhoneNumber.Of("+5581999990000"), "bike", -8.0, -34.9);

        delivery.ApplyProviderEvent("evt-1", "pickup", DeliveryStatus.Pickup, Now.AddMinutes(1), courier, Now);

        delivery.Courier.ShouldNotBeNull().PhoneMasked.ShouldBe("+55*******0000");
    }

    private static DeliveryEventDisposition Apply(Delivery delivery, string eventId, DeliveryStatus status, int minutes) =>
        delivery.ApplyProviderEvent(eventId, status.ToString().ToLowerInvariant(), status, Now.AddMinutes(minutes), null, Now.AddMinutes(minutes));
}
