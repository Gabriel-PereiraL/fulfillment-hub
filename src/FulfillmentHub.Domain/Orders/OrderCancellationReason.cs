namespace FulfillmentHub.Domain.Orders;

public enum OrderCancellationReason
{
    CustomerRequest = 0,
    PaymentFailed = 1,
    DeliveryFailed = 2,
    OperatorAction = 3,
    StockUnavailable = 4,
    PaymentTimeout = 5,
}
