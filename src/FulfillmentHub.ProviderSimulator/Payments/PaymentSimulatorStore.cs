using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Payments;

/// <summary>
/// In-memory "database" of the simulated payment provider: payments, idempotency keys and the settlement schedule.
/// Thread-safe; state is lost on restart by design.
/// </summary>
public sealed class PaymentSimulatorStore(IOptionsMonitor<PaymentSimulatorOptions> options, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, SimulatedPayment> _payments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string PaymentId, string RequestHash, DateTimeOffset ExpiresAt)> _idempotency = new(StringComparer.Ordinal);

    public enum CreateOutcome
    {
        Created,
        Replayed,
        IdempotencyConflict,
    }

    public (CreateOutcome Outcome, SimulatedPayment Payment) Create(string idempotencyKey, CreatePaymentRequest request)
    {
        var now = timeProvider.GetUtcNow();
        var requestHash = $"{request.Amount}|{request.Currency}|{request.OrderReference}|{request.CustomerReference}|{request.Capture}";

        if (_idempotency.TryGetValue(idempotencyKey, out var existing) && existing.ExpiresAt > now)
        {
            var payment = _payments[existing.PaymentId];
            return existing.RequestHash == requestHash
                ? (CreateOutcome.Replayed, payment)
                : (CreateOutcome.IdempotencyConflict, payment);
        }

        var settings = options.CurrentValue;
        var scenario = request.Scenario != PaymentScenario.Default ? request.Scenario : ScenarioFromAmount(request.Amount);
        var approve = scenario switch
        {
            PaymentScenario.Approve or PaymentScenario.SilentApprove => true,
            PaymentScenario.Decline => false,
            _ => Random.Shared.NextDouble() < settings.ApprovalRate,
        };

        var created = new SimulatedPayment
        {
            Id = "pay_" + Guid.CreateVersion7().ToString("N")[..20],
            Amount = request.Amount,
            Currency = request.Currency.ToLowerInvariant(),
            OrderReference = request.OrderReference,
            CustomerReference = request.CustomerReference,
            CreatedAt = now,
            UpdatedAt = now,
            Status = "pending",
            SettleAt = now.AddMilliseconds(settings.SettleDelayMs),
            WillApprove = approve,
            SendWebhooks = scenario != PaymentScenario.SilentApprove,
        };

        // First writer wins the idempotency slot; a concurrent duplicate is answered as a replay.
        var slot = _idempotency.GetOrAdd(idempotencyKey, _ => (created.Id, requestHash, now.AddMinutes(settings.IdempotencyTtlMinutes)));
        if (slot.PaymentId != created.Id)
        {
            var winner = _payments[slot.PaymentId];
            return slot.RequestHash == requestHash ? (CreateOutcome.Replayed, winner) : (CreateOutcome.IdempotencyConflict, winner);
        }

        _payments[created.Id] = created;
        return (CreateOutcome.Created, created);
    }

    /// <summary>
    /// Sandbox convention, like real providers' "magic" test amounts: cents ending in 99 are declined, cents ending in 98
    /// are approved silently (no webhook, so reconciliation has to notice). Everything else follows the configured rate.
    /// </summary>
    public static PaymentScenario ScenarioFromAmount(long amount) => (amount % 100) switch
    {
        99 => PaymentScenario.Decline,
        98 => PaymentScenario.SilentApprove,
        _ => PaymentScenario.Default,
    };

    public SimulatedPayment? Find(string id) => _payments.GetValueOrDefault(id);

    /// <summary>Payments whose settlement time has passed and that were not settled yet.</summary>
    public IReadOnlyList<SimulatedPayment> TakeDueForSettlement()
    {
        var now = timeProvider.GetUtcNow();
        var due = new List<SimulatedPayment>();

        foreach (var payment in _payments.Values)
        {
            lock (payment)
            {
                if (!payment.Settled && payment.SettleAt <= now && payment.Status is "pending" or "authorized")
                {
                    payment.Settled = true;
                    payment.Status = payment.WillApprove ? "paid" : "failed";
                    payment.FailureCode = payment.WillApprove ? null : options.CurrentValue.DeclineCode;
                    payment.UpdatedAt = now;
                    due.Add(payment);
                }
            }
        }

        return due;
    }

    public RefundResponse? Refund(string paymentId, long? amount)
    {
        if (!_payments.TryGetValue(paymentId, out var payment))
        {
            return null;
        }

        lock (payment)
        {
            if (payment.Status != "paid")
            {
                return null;
            }

            var refundAmount = amount ?? payment.Amount - payment.RefundedAmount;
            payment.RefundedAmount += refundAmount;
            payment.Status = payment.RefundedAmount >= payment.Amount ? "refunded" : "paid";
            payment.UpdatedAt = timeProvider.GetUtcNow();

            return new RefundResponse("ref_" + Guid.CreateVersion7().ToString("N")[..20], payment.Id, "succeeded", refundAmount, payment.UpdatedAt);
        }
    }
}
