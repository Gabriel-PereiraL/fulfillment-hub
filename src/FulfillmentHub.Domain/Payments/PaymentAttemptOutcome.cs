namespace FulfillmentHub.Domain.Payments;

public enum PaymentAttemptOutcome
{
    Pending = 0,
    Succeeded = 1,
    TransientFailure = 2,
    PermanentFailure = 3,
}
