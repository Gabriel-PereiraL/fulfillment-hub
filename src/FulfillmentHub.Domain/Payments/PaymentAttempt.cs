namespace FulfillmentHub.Domain.Payments;

/// <summary>One call to the payment provider, recorded inside the <see cref="Payment"/> aggregate.</summary>
public sealed class PaymentAttempt
{
    public const int ErrorCodeMaxLength = 64;
    public const int ProviderReferenceMaxLength = 128;

    private PaymentAttempt()
    {
    }

    public Guid Id { get; private set; }

    public int Number { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public PaymentAttemptOutcome Outcome { get; private set; }

    public string? ProviderErrorCode { get; private set; }

    public string? ProviderReference { get; private set; }

    internal static PaymentAttempt Start(int number, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Number = number,
        RequestedAt = now,
        Outcome = PaymentAttemptOutcome.Pending,
    };

    internal void Complete(PaymentAttemptOutcome outcome, string? providerReference, string? errorCode, DateTimeOffset now)
    {
        Outcome = outcome;
        ProviderReference = Truncate(providerReference, ProviderReferenceMaxLength);
        ProviderErrorCode = Truncate(errorCode, ErrorCodeMaxLength);
        CompletedAt = now;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value[..Math.Min(value.Length, maxLength)];
}
