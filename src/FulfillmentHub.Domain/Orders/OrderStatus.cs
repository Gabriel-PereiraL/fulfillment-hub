namespace FulfillmentHub.Domain.Orders;

public enum OrderStatus
{
    Created = 0,
    AwaitingPayment = 1,
    Paid = 2,
    DeliveryRequested = 3,
    InDelivery = 4,
    Delivered = 5,
    Cancelled = 6,
}
