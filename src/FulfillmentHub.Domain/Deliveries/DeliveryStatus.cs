namespace FulfillmentHub.Domain.Deliveries;

/// <summary>
/// Delivery statuses mirrored from the provider. The numeric value is the canonical order used to decide whether an
/// incoming provider event moves the delivery forward; final statuses are handled explicitly.
/// </summary>
public enum DeliveryStatus
{
    Requested = 0,
    Pending = 1,
    Pickup = 2,
    PickupComplete = 3,
    Dropoff = 4,
    Delivered = 5,
    Cancelled = 6,
    Returned = 7,
}
