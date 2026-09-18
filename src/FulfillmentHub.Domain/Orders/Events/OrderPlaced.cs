using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;

namespace FulfillmentHub.Domain.Orders.Events;

public sealed record OrderPlaced(OrderId OrderId, CustomerId CustomerId, Money Total, DateTimeOffset OccurredAt) : IDomainEvent;
