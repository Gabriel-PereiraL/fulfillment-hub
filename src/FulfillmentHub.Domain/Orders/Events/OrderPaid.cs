using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Payments;

namespace FulfillmentHub.Domain.Orders.Events;

public sealed record OrderPaid(OrderId OrderId, PaymentId PaymentId, DateTimeOffset OccurredAt) : IDomainEvent;
