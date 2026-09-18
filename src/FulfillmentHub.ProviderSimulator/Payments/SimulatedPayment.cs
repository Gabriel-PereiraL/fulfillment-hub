namespace FulfillmentHub.ProviderSimulator.Payments;

/// <summary>In-memory state of a simulated payment (D-P8: the simulator does not persist; a restart clears it).</summary>
public sealed class SimulatedPayment
{
    public required string Id { get; init; }

    public required long Amount { get; init; }

    public required string Currency { get; init; }

    public required string OrderReference { get; init; }

    public string? CustomerReference { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>pending | authorized | paid | failed | refunded</summary>
    public string Status { get; set; } = "pending";

    public string? FailureCode { get; set; }

    public required DateTimeOffset SettleAt { get; init; }

    public required bool WillApprove { get; init; }

    public required bool SendWebhooks { get; init; }

    public bool Settled { get; set; }

    public long RefundedAmount { get; set; }

    public PaymentResponse ToResponse() => new(Id, Status, Amount, Currency, OrderReference, FailureCode, CreatedAt, UpdatedAt);
}
