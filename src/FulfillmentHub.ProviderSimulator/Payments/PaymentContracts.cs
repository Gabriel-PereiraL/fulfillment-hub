using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.ProviderSimulator.Payments;

/// <summary>Per-request scenario override (development aid). Absent = follow <see cref="PaymentSimulatorOptions"/>.</summary>
public enum PaymentScenario
{
    Default = 0,
    Approve = 1,
    Decline = 2,
    /// <summary>Settle but never send the webhook (exercises reconciliation).</summary>
    SilentApprove = 3,
}

public sealed record CreatePaymentRequest(
    [property: Range(1, 100_000_000)] long Amount,
    [property: Required, StringLength(3, MinimumLength = 3)] string Currency,
    [property: Required, MaxLength(64)] string OrderReference,
    [property: MaxLength(64)] string? CustomerReference,
    bool Capture = true,
    PaymentScenario Scenario = PaymentScenario.Default);

public sealed record PaymentResponse(
    string Id,
    string Status,
    long Amount,
    string Currency,
    string OrderReference,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record RefundRequest([property: Range(1, 100_000_000)] long? Amount);

public sealed record RefundResponse(string RefundId, string PaymentId, string Status, long Amount, DateTimeOffset CreatedAt);

public sealed record PaymentWebhookEvent(string Id, string Type, DateTimeOffset CreatedAt, PaymentWebhookData Data);

public sealed record PaymentWebhookData(string PaymentId, string Status, string? FailureCode, string OrderReference, DateTimeOffset OccurredAt);
