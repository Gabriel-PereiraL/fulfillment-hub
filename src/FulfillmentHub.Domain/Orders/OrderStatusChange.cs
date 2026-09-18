using FulfillmentHub.Domain.Identity;

namespace FulfillmentHub.Domain.Orders;

/// <summary>Immutable audit entry of a status transition inside the <see cref="Order"/> aggregate.</summary>
public sealed class OrderStatusChange
{
    public const int ReasonMaxLength = 200;

    private OrderStatusChange()
    {
    }

    public Guid Id { get; private set; }

    public OrderStatus From { get; private set; }

    public OrderStatus To { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string? Reason { get; private set; }

    public UserId? ActorUserId { get; private set; }

    internal static OrderStatusChange Create(OrderStatus from, OrderStatus to, DateTimeOffset at, string? reason, UserId? actor) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            From = from,
            To = to,
            At = at,
            Reason = reason is null ? null : reason[..Math.Min(reason.Length, ReasonMaxLength)],
            ActorUserId = actor,
        };
}
