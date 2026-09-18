using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Orders.Events;

public sealed record OrderDelivered(OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
