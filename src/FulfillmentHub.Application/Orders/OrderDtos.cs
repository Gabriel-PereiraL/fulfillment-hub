using FulfillmentHub.Application.Common;
using FulfillmentHub.Domain.Orders;

namespace FulfillmentHub.Application.Orders;

public sealed record PlaceOrderCommand(IReadOnlyList<PlaceOrderLine> Lines, AddressDto DeliveryAddress, string? IdempotencyKey);

public sealed record PlaceOrderLine(Guid ProductId, int Quantity);

public sealed record OrderItemDto(Guid ProductId, string Sku, string ProductName, MoneyDto UnitPrice, int Quantity, MoneyDto LineTotal);

public sealed record OrderStatusChangeDto(string From, string To, DateTimeOffset At, string? Reason);

public sealed record OrderDto(
    Guid Id,
    long Number,
    Guid CustomerId,
    string Status,
    string? CancellationReason,
    IReadOnlyList<OrderItemDto> Items,
    AddressDto DeliveryAddress,
    MoneyDto Subtotal,
    MoneyDto? DeliveryFee,
    MoneyDto Total,
    Guid? PaymentId,
    Guid? DeliveryId,
    IReadOnlyList<OrderStatusChangeDto> StatusHistory,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OrderDto From(Order order) => new(
        order.Id.Value,
        order.Number,
        order.CustomerId.Value,
        order.Status.ToString(),
        order.CancellationReason?.ToString(),
        order.Items.Select(i => new OrderItemDto(
            i.ProductId.Value, i.Sku, i.ProductName, MoneyDto.From(i.UnitPrice), i.Quantity, MoneyDto.From(i.LineTotal))).ToList(),
        AddressDto.From(order.DeliveryAddress),
        MoneyDto.From(order.Subtotal),
        MoneyDto.FromOptional(order.DeliveryFee),
        MoneyDto.From(order.Total),
        order.PaymentId?.Value,
        order.DeliveryId?.Value,
        order.StatusHistory.OrderBy(h => h.At).Select(h => new OrderStatusChangeDto(h.From.ToString(), h.To.ToString(), h.At, h.Reason)).ToList(),
        order.CreatedAt,
        order.UpdatedAt);
}

public sealed record OrderSummaryDto(Guid Id, long Number, string Status, MoneyDto Total, int ItemCount, DateTimeOffset CreatedAt);

public sealed record OrderPage(IReadOnlyList<OrderSummaryDto> Items, string? NextCursor);
