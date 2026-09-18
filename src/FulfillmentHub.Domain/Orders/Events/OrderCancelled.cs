using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Orders.Events;

public sealed record OrderCancelled(
    OrderId OrderId,
    OrderCancellationReason Reason,
    OrderStatus PreviousStatus,
    DateTimeOffset OccurredAt) : IDomainEvent;
