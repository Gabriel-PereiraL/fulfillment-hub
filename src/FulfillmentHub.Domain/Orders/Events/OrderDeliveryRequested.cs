using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;

namespace FulfillmentHub.Domain.Orders.Events;

public sealed record OrderDeliveryRequested(OrderId OrderId, DeliveryId DeliveryId, DateTimeOffset OccurredAt) : IDomainEvent;
