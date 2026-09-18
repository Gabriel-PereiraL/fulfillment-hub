using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments;

namespace FulfillmentHub.UnitTests.Domain;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    [Fact]
    public void Create_StartsPendingWithDeterministicProviderIdempotencyKey()
    {
        var orderId = OrderId.New();

        var payment = Payment.Create(orderId, Money.Of(25m), "simulated-psp", Now);
        var again = Payment.Create(orderId, Money.Of(25m), "simulated-psp", Now);

        payment.Status.ShouldBe(PaymentStatus.Pending);
        payment.ProviderIdempotencyKey.ShouldBe(again.ProviderIdempotencyKey, "retries for the same order must reuse the key");
    }

    [Fact]
    public void Create_WithNonPositiveAmount_Throws()
    {
        Should.Throw<DomainException>(() => Payment.Create(OrderId.New(), Money.Zero(), "psp", Now));
    }

    [Fact]
    public void Attempts_AreNumberedAndOnlyOnePendingAtATime()
    {
        var payment = TestData.Payment();

        var first = payment.StartAttempt(Now);
        Should.Throw<DomainException>(() => payment.StartAttempt(Now));

        payment.CompleteAttempt(first.Id, PaymentAttemptOutcome.TransientFailure, null, "timeout", Now.AddSeconds(5));
        var second = payment.StartAttempt(Now.AddSeconds(6));
        payment.CompleteAttempt(second.Id, PaymentAttemptOutcome.Succeeded, "pay_123", null, Now.AddSeconds(7));

        payment.Attempts.Select(a => a.Number).ShouldBe([1, 2]);
        payment.Attempts[0].Outcome.ShouldBe(PaymentAttemptOutcome.TransientFailure);
        payment.ProviderPaymentId.ShouldBe("pay_123");
    }

    [Fact]
    public void ApplyProviderStatus_FollowsStateMachine()
    {
        var payment = TestData.Payment();

        payment.ApplyProviderStatus(PaymentStatus.Authorized, Now.AddMinutes(1), null, Now).ShouldBeTrue();
        payment.ApplyProviderStatus(PaymentStatus.Paid, Now.AddMinutes(2), null, Now).ShouldBeTrue();
        payment.ApplyProviderStatus(PaymentStatus.Refunded, Now.AddMinutes(3), null, Now).ShouldBeTrue();

        payment.Status.ShouldBe(PaymentStatus.Refunded);
        payment.IsFinal.ShouldBeTrue();
    }

    [Fact]
    public void ApplyProviderStatus_IgnoresStaleAndDuplicateEvents()
    {
        var payment = TestData.Payment();
        payment.ApplyProviderStatus(PaymentStatus.Paid, Now.AddMinutes(5), null, Now);

        var staleAuthorized = payment.ApplyProviderStatus(PaymentStatus.Authorized, Now.AddMinutes(1), null, Now);
        var duplicatePaid = payment.ApplyProviderStatus(PaymentStatus.Paid, Now.AddMinutes(5), null, Now);

        staleAuthorized.ShouldBeFalse();
        duplicatePaid.ShouldBeFalse();
        payment.Status.ShouldBe(PaymentStatus.Paid);
    }

    [Theory]
    [InlineData(PaymentStatus.Failed, PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Refunded, PaymentStatus.Paid)]
    [InlineData(PaymentStatus.Cancelled, PaymentStatus.Authorized)]
    [InlineData(PaymentStatus.Pending, PaymentStatus.Refunded)]
    public void InvalidTransitions_Throw(PaymentStatus from, PaymentStatus attempted)
    {
        var payment = TestData.Payment();
        if (from != PaymentStatus.Pending)
        {
            if (from == PaymentStatus.Refunded)
            {
                payment.MarkPaid(Now);
            }

            payment.ApplyProviderStatus(from, Now.AddMinutes(1), "declined", Now);
        }

        Action act = () => _ = payment.ApplyProviderStatus(attempted, Now.AddMinutes(2), null, Now);

        act.ShouldThrow<InvalidStateTransitionException>();
        payment.Status.ShouldBe(from);
    }

    [Fact]
    public void Fail_RecordsReason()
    {
        var payment = TestData.Payment();

        payment.ApplyProviderStatus(PaymentStatus.Failed, Now.AddMinutes(1), "card_declined", Now);

        payment.Status.ShouldBe(PaymentStatus.Failed);
        payment.FailureReason.ShouldBe("card_declined");
        Should.Throw<DomainException>(() => payment.StartAttempt(Now));
    }
}
