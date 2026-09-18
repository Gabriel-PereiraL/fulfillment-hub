namespace FulfillmentHub.Domain.Payments;

public enum PaymentStatus
{
    Pending = 0,
    Authorized = 1,
    Paid = 2,
    Failed = 3,
    Cancelled = 4,
    Refunded = 5,
}
