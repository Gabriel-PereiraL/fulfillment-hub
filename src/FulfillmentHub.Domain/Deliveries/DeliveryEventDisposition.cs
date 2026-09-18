namespace FulfillmentHub.Domain.Deliveries;

/// <summary>What the aggregate did with a provider event.</summary>
public enum DeliveryEventDisposition
{
    Applied = 0,
    Duplicate = 1,
    OutOfOrder = 2,
    Stale = 3,
    Conflict = 4,
}
