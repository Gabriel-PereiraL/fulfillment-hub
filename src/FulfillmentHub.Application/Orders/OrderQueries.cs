using System.Buffers.Binary;
using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Application.Orders;

/// <summary>
/// Read side of orders. Customers only ever see their own orders (a foreign order is reported as not found, D-18);
/// operators and administrators see everything. Listing uses keyset pagination on the unique, monotonic order
/// number (descending), so deep pages cost the same as the first one.
/// </summary>
public sealed class OrderQueries(IFulfillmentHubDbContext db, ICurrentUser currentUser)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 20;

    public async Task<OrderDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(id);
        var order = await Visible().AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        return order is null ? null : OrderDto.From(order);
    }

    public async Task<Result<OrderPage>> ListAsync(string? cursor, int? pageSize, Guid? customerId, CancellationToken cancellationToken)
    {
        var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        if (!TryDecodeCursor(cursor, out var after))
        {
            return Failure.Validation("orders.invalid_cursor", "The pagination cursor is not valid.");
        }

        var query = Visible().AsNoTracking();

        if (customerId is { } filter)
        {
            var filterId = CustomerId.From(filter);
            query = query.Where(o => o.CustomerId == filterId);
        }

        if (after is { } lastSeenNumber)
        {
            query = query.Where(o => o.Number < lastSeenNumber);
        }

        var rows = await query
            .OrderByDescending(o => o.Number)
            .Take(size + 1)
            .Select(o => new
            {
                o.Id,
                o.Number,
                o.Status,
                o.Total,
                ItemCount = o.Items.Count,
                o.CreatedAt,
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > size;
        var page = rows.Take(size).ToList();
        var items = page
            .Select(r => new OrderSummaryDto(r.Id.Value, r.Number, r.Status.ToString(), MoneyDto.From(r.Total), r.ItemCount, r.CreatedAt))
            .ToList();
        var next = hasMore ? EncodeCursor(page[^1].Number) : null;

        return Result.Ok(new OrderPage(items, next));
    }

    private IQueryable<Order> Visible()
    {
        if (currentUser.IsInRole(Role.Operator) || currentUser.IsInRole(Role.Admin))
        {
            return db.Orders;
        }

        var customerId = currentUser.CustomerId ?? CustomerId.From(Guid.Empty);
        return db.Orders.Where(o => o.CustomerId == customerId);
    }

    // Cursor = base64url(order number as 8 big-endian bytes): opaque to clients, cheap to validate.
    private static string EncodeCursor(long number)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, number);
        return Convert.ToBase64String(buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? cursor, out long? number)
    {
        number = null;

        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        if (cursor.Length > 16)
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var bytes = Convert.FromBase64String(padded);

            if (bytes.Length != 8)
            {
                return false;
            }

            number = BinaryPrimitives.ReadInt64BigEndian(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
