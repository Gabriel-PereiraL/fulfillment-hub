using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Orders;

namespace FulfillmentHub.Domain.Payments.Events;

/// <summary>The provider confirmed the money was taken. Consumers check whether the order still wants it (late capture → refund).</summary>
public sealed record PaymentPaid(PaymentId PaymentId, OrderId OrderId, DateTimeOffset OccurredAt) : IDomainEvent;
