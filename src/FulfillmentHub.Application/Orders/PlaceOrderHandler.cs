using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Catalog;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Orders;

/// <summary>
/// Places an order for the current customer: validates the lines, reserves stock and persists the order and the
/// reservation in one transaction. Stock is protected by optimistic concurrency (xmin): a concurrent reservation
/// surfaces as <see cref="DbUpdateConcurrencyException"/>, the products are reloaded and the reservation is retried
/// a few times before giving up with a conflict — so two buyers never share the last unit.
/// </summary>
public sealed partial class PlaceOrderHandler(
    IFulfillmentHubDbContext db,
    ICurrentUser currentUser,
    OrdersMetrics metrics,
    TimeProvider timeProvider,
    ILogger<PlaceOrderHandler> logger)
{
    private const int MaxReservationAttempts = 3;

    public async Task<Result<OrderDto>> HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken)
    {
        if (currentUser.CustomerId is not { } customerId)
        {
            return Failure.Forbidden("order.no_customer_profile", "The current user has no customer profile.");
        }

        if (command.Lines.Count == 0)
        {
            return Failure.Validation("order.no_items", "An order must have at least one item.");
        }

        if (command.Lines.Select(l => l.ProductId).Distinct().Count() != command.Lines.Count)
        {
            return Failure.Validation("order.duplicate_product", "An order cannot contain the same product more than once.");
        }

        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken);
        if (customer is null || !customer.IsActive)
        {
            return Failure.Forbidden("order.customer_inactive", "The customer profile is not active.");
        }

        Address deliveryAddress;
        try
        {
            var a = command.DeliveryAddress;
            deliveryAddress = Address.Create(a.Street, a.Number, a.Complement, a.District, a.City, a.State, a.PostalCode, a.Country, a.Latitude, a.Longitude);
        }
        catch (DomainException exception)
        {
            return Failure.Validation("order.invalid_address", exception.Message);
        }

        var productIds = command.Lines.Select(l => ProductId.From(l.ProductId)).ToList();

        for (var attempt = 1; attempt <= MaxReservationAttempts; attempt++)
        {
            var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);

            var missing = productIds.Except(products.Select(p => p.Id)).ToList();
            if (missing.Count > 0)
            {
                return Failure.NotFound("order.product_not_found", $"Unknown product(s): {string.Join(", ", missing.Select(m => m.Value))}.");
            }

            var lines = command.Lines
                .Select(l => new OrderLine(products.Single(p => p.Id == ProductId.From(l.ProductId)), l.Quantity))
                .ToList();

            Order order;
            try
            {
                var now = timeProvider.GetUtcNow();
                order = Order.Place(customer, deliveryAddress, lines, command.IdempotencyKey, now);

                foreach (var line in lines)
                {
                    line.Product.Reserve(line.Quantity, now);
                }
            }
            catch (InsufficientStockException exception)
            {
                metrics.ReservationConflict("insufficient_stock");
                return Failure.Conflict("order.insufficient_stock", exception.Message);
            }
            catch (DomainException exception)
            {
                return Failure.Validation("order.invalid", exception.Message);
            }

            db.Orders.Add(order);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxReservationAttempts)
            {
                // Someone else reserved from the same product between our read and our write: start over with fresh rows.
                metrics.ReservationConflict("concurrent_update");
                LogReservationRetry(attempt);
                db.ChangeTracker.Clear();
                continue;
            }
            catch (DbUpdateConcurrencyException)
            {
                metrics.ReservationConflict("concurrent_update");
                return Failure.Conflict("order.stock_conflict", "Stock changed while placing the order. Please try again.");
            }

            metrics.OrderPlaced();
            LogOrderPlaced(order.Id, order.Number, order.Items.Count);
            return Result.Ok(OrderDto.From(order));
        }

        throw new InvalidOperationException("Unreachable: the reservation loop always returns.");
    }

    [LoggerMessage(EventId = 4000, Level = LogLevel.Information, Message = "Order {OrderId} (#{Number}) placed with {ItemCount} items")]
    private partial void LogOrderPlaced(OrderId orderId, long number, int itemCount);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Stock reservation hit a concurrent update, retrying (attempt {Attempt})")]
    private partial void LogReservationRetry(int attempt);
}
