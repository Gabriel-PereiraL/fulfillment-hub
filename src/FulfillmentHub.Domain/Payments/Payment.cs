using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Orders;
using FulfillmentHub.Domain.Payments.Events;

namespace FulfillmentHub.Domain.Payments;

/// <summary>
/// Payment aggregate: one active payment per order (enforced by a partial unique index), provider attempts and the
/// status state machine of docs/DOMAIN.md §6. Provider events older than the last applied one are ignored.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>
{
    public const int ProviderMaxLength = 40;
    public const int ProviderPaymentIdMaxLength = 128;
    public const int FailureReasonMaxLength = 200;

    private static readonly Dictionary<PaymentStatus, PaymentStatus[]> AllowedTransitions =
        new Dictionary<PaymentStatus, PaymentStatus[]>
        {
            [PaymentStatus.Pending] = [PaymentStatus.Authorized, PaymentStatus.Paid, PaymentStatus.Failed, PaymentStatus.Cancelled],
            [PaymentStatus.Authorized] = [PaymentStatus.Paid, PaymentStatus.Failed, PaymentStatus.Cancelled],
            [PaymentStatus.Paid] = [PaymentStatus.Refunded],
            [PaymentStatus.Failed] = [],
            [PaymentStatus.Cancelled] = [],
            [PaymentStatus.Refunded] = [],
        };

    private readonly List<PaymentAttempt> _attempts = [];

    private Payment(PaymentId id)
        : base(id)
    {
    }

    public OrderId OrderId { get; private set; }

    public Money Amount { get; private set; } = null!;

    public PaymentStatus Status { get; private set; }

    public string Provider { get; private set; } = null!;

    public string? ProviderPaymentId { get; private set; }

    /// <summary>Key sent to the provider so that retries never create a second charge (ADR-010).</summary>
    public string ProviderIdempotencyKey { get; private set; } = null!;

    public string? FailureReason { get; private set; }

    public DateTimeOffset? LastProviderEventAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>By attempt number. Rows come back from the database in no guaranteed order, so the aggregate sorts.</summary>
    public IReadOnlyList<PaymentAttempt> Attempts => _attempts.OrderBy(a => a.Number).ToList();

    public bool IsFinal => Status is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded;

    public static Payment Create(OrderId orderId, Money amount, string provider, DateTimeOffset now)
    {
        if (amount.Amount <= 0m)
        {
            throw new DomainException("Payment amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(provider) || provider.Length > ProviderMaxLength)
        {
            throw new DomainException("Payment provider name is required.");
        }

        return new Payment(PaymentId.New())
        {
            OrderId = orderId,
            Amount = amount,
            Status = PaymentStatus.Pending,
            Provider = provider,
            ProviderIdempotencyKey = $"order-{orderId.Value:N}",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Registers a new call to the provider. Only one attempt can be pending at a time.</summary>
    public PaymentAttempt StartAttempt(DateTimeOffset now)
    {
        if (IsFinal || Status == PaymentStatus.Paid)
        {
            throw new DomainException($"Cannot start a payment attempt while the payment is '{Status}'.");
        }

        if (_attempts.Any(a => a.Outcome == PaymentAttemptOutcome.Pending))
        {
            throw new DomainException("A payment attempt is already in progress.");
        }

        var attempt = PaymentAttempt.Start(_attempts.Count + 1, now);
        _attempts.Add(attempt);
        UpdatedAt = now;

        return attempt;
    }

    public void CompleteAttempt(
        Guid attemptId,
        PaymentAttemptOutcome outcome,
        string? providerReference,
        string? errorCode,
        DateTimeOffset now)
    {
        if (outcome == PaymentAttemptOutcome.Pending)
        {
            throw new DomainException("An attempt cannot be completed as pending.");
        }

        var attempt = _attempts.FirstOrDefault(a => a.Id == attemptId)
            ?? throw new DomainException($"Attempt '{attemptId}' does not belong to payment '{Id}'.");

        attempt.Complete(outcome, providerReference, errorCode, now);

        if (outcome == PaymentAttemptOutcome.Succeeded && providerReference is not null)
        {
            ProviderPaymentId = providerReference;
        }

        UpdatedAt = now;
    }

    /// <summary>Applies a provider status notification. Returns false when the event is stale or a no-op.</summary>
    public bool ApplyProviderStatus(PaymentStatus reported, DateTimeOffset providerEventAt, string? failureReason, DateTimeOffset now)
    {
        if (LastProviderEventAt is { } last && providerEventAt < last)
        {
            return false;
        }

        if (reported == Status)
        {
            LastProviderEventAt = providerEventAt;
            return false;
        }

        switch (reported)
        {
            case PaymentStatus.Authorized:
                Transition(PaymentStatus.Authorized, now);
                break;
            case PaymentStatus.Paid:
                Transition(PaymentStatus.Paid, now);
                Raise(new PaymentPaid(Id, OrderId, now));
                break;
            case PaymentStatus.Failed:
                Fail(failureReason ?? "Provider reported failure", now);
                break;
            case PaymentStatus.Refunded:
                MarkRefunded(now);
                break;
            case PaymentStatus.Cancelled:
                Cancel(now);
                break;
            case PaymentStatus.Pending:
                throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), reported.ToString());
            default:
                throw new DomainException($"Unknown payment status '{reported}'.");
        }

        LastProviderEventAt = providerEventAt;
        return true;
    }

    public void MarkAuthorized(DateTimeOffset now) => Transition(PaymentStatus.Authorized, now);

    public void MarkPaid(DateTimeOffset now) => Transition(PaymentStatus.Paid, now);

    public void Fail(string reason, DateTimeOffset now)
    {
        Transition(PaymentStatus.Failed, now);
        FailureReason = reason[..Math.Min(reason.Length, FailureReasonMaxLength)];
    }

    public void Cancel(DateTimeOffset now) => Transition(PaymentStatus.Cancelled, now);

    public void MarkRefunded(DateTimeOffset now) => Transition(PaymentStatus.Refunded, now);

    private void Transition(PaymentStatus to, DateTimeOffset now)
    {
        if (!AllowedTransitions[Status].Contains(to))
        {
            throw new InvalidStateTransitionException(nameof(Payment), Status.ToString(), to.ToString());
        }

        Status = to;
        UpdatedAt = now;
    }
}
