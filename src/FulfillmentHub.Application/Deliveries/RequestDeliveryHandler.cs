using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Application.Orders;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Application.Deliveries;

public sealed record RequestDeliveryCommand(Guid OrderId);

public enum DeliveryRequestOutcome
{
    /// <summary>A delivery was created at the provider in this call.</summary>
    Requested = 0,

    /// <summary>The provider already had it (duplicate / earlier timed-out call); the existing one was adopted.</summary>
    Adopted = 1,

    /// <summary>The order already had an active delivery; nothing to do.</summary>
    AlreadyRequested = 2,
}

/// <summary>
/// Creates the delivery for a paid order: reuses the checkout quote while valid, requotes once when it expired, creates
/// the delivery with a durable idempotency key, reconciles <c>409 duplicate_delivery</c> by adopting the existing
/// delivery, and moves the order to <c>DeliveryRequested</c>. Triggered by the <c>OrderPaid</c> outbox message and, as a
/// safety net, by the Worker's periodic sweep (D-52/D-68). Permanent provider rejections cancel the order (<c>DeliveryFailed</c>) and release stock.
/// </summary>
public sealed partial class RequestDeliveryHandler(
    IFulfillmentHubDbContext db,
    IDeliveryProviderClient provider,
    IOptions<FulfillmentOriginOptions> origin,
    DeliveriesMetrics metrics,
    TimeProvider timeProvider,
    ILogger<RequestDeliveryHandler> logger)
{
    private const int MaxQuoteAttempts = 2;

    public async Task<Result<DeliveryRequestOutcome>> HandleAsync(RequestDeliveryCommand command, CancellationToken cancellationToken)
    {
        var orderId = OrderId.From(command.OrderId);
        var order = await db.Orders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return Failure.NotFound("order.not_found", "Order not found.");
        }

        if (order.Status is OrderStatus.DeliveryRequested or OrderStatus.InDelivery or OrderStatus.Delivered)
        {
            return Result.Ok(DeliveryRequestOutcome.AlreadyRequested);
        }

        if (order.Status != OrderStatus.Paid)
        {
            return Failure.Conflict("order.not_paid", $"A delivery can only be requested for a paid order (status is '{order.Status}').");
        }

        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == order.CustomerId, cancellationToken);
        var pickup = origin.Value.ToParty();
        var dropoff = new DeliveryParty(customer.Name, order.DeliveryAddress, customer.Phone);
        var now = timeProvider.GetUtcNow();

        var deliveries = await db.Deliveries.Where(d => d.OrderId == order.Id).ToListAsync(cancellationToken);
        var active = deliveries.FirstOrDefault(d => d.IsActive);

        if (active is { ProviderDeliveryId: not null })
        {
            // Created earlier but the order transition was lost (crash between the two saves): just catch the order up.
            order.MarkDeliveryRequested(active.Id, order.DeliveryFee ?? active.Fee, now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Ok(DeliveryRequestOutcome.AlreadyRequested);
        }

        var quotes = await db.DeliveryQuotes.Where(q => q.OrderId == order.Id).OrderByDescending(q => q.CreatedAt).ToListAsync(cancellationToken);

        for (var quoteAttempt = 1; quoteAttempt <= MaxQuoteAttempts; quoteAttempt++)
        {
            // A delivery whose creation call never completed keeps its idempotency key, so the provider deduplicates.
            var delivery = active;
            var quote = delivery is null ? null : quotes.SingleOrDefault(q => q.Id == delivery.QuoteId);

            if (delivery is null || quote is null || quote.IsExpired(now))
            {
                if (delivery is not null)
                {
                    delivery.Cancel(now); // never created at the provider; superseded by a new attempt
                    active = null;
                }

                var quoteResult = await ResolveQuoteAsync(order, quotes, pickup, dropoff, now, cancellationToken);
                if (!quoteResult.IsSuccess)
                {
                    return await FailPermanentlyOrDeferAsync(order, quoteResult.Failure, now, cancellationToken);
                }

                quote = quoteResult.Value;
                delivery = Delivery.Request(quote, deliveries.Count + 1, now);
                deliveries.Add(delivery);
                db.Deliveries.Add(delivery);
                await db.SaveChangesAsync(cancellationToken); // the idempotency key is durable before the provider call
            }

            var request = new GatewayCreateDelivery(
                delivery.ProviderIdempotencyKey,
                quote.ProviderQuoteId,
                pickup,
                dropoff,
                order.Items.Select(i => new GatewayManifestItem(i.ProductName, i.Quantity, i.UnitPrice)).ToList(),
                ManifestReference: $"FH-{order.Number}",
                order.Subtotal);

            var created = await provider.CreateAsync(request, cancellationToken);

            if (created.IsSuccess)
            {
                return await ConfirmAsync(order, delivery, created.Value, DeliveryRequestOutcome.Requested, now, cancellationToken);
            }

            var failure = created.Failure;

            if (failure.Code == "provider.duplicate_delivery"
                && failure.Metadata?.GetValueOrDefault(IDeliveryProviderClient.DuplicateDeliveryIdKey) is { } existingId)
            {
                var existing = await provider.GetAsync(existingId, cancellationToken);
                if (!existing.IsSuccess)
                {
                    return existing.Failure;
                }

                metrics.Requested("adopted_duplicate");
                return await ConfirmAsync(order, delivery, existing.Value, DeliveryRequestOutcome.Adopted, now, cancellationToken);
            }

            if (failure.Code is "provider.expired_quote" or "provider.used_quote")
            {
                LogQuoteRejected(order.Id, failure.Code, quoteAttempt);
                active = delivery; // forces a fresh quote (and a new idempotency key) on the next loop
                quotes.Remove(quote);
                now = timeProvider.GetUtcNow();
                continue;
            }

            return await FailPermanentlyOrDeferAsync(order, failure, now, cancellationToken);
        }

        metrics.Requested("quote_expired_twice");
        LogGaveUp(order.Id);
        return Failure.Conflict("delivery.quote_expired", "The delivery quote expired twice; the order needs operator attention.");
    }

    private async Task<Result<DeliveryQuote>> ResolveQuoteAsync(
        Order order,
        List<DeliveryQuote> quotes,
        DeliveryParty pickup,
        DeliveryParty dropoff,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var valid = quotes.FirstOrDefault(q => !q.IsExpired(now));
        if (valid is not null)
        {
            return Result.Ok(valid);
        }

        var quoted = await provider.QuoteAsync(new GatewayQuoteRequest(pickup, dropoff, order.Subtotal), cancellationToken);
        if (!quoted.IsSuccess)
        {
            return quoted.Failure;
        }

        var quote = CheckoutDeliveryQuoter.ToAggregate(order.Id, provider.ProviderName, quoted.Value, now);
        db.DeliveryQuotes.Add(quote);
        quotes.Insert(0, quote);
        metrics.Quote("requoted");
        LogRequoted(order.Id, quote.Fee.Amount);
        return Result.Ok(quote);
    }

    private async Task<Result<DeliveryRequestOutcome>> ConfirmAsync(
        Order order,
        Delivery delivery,
        GatewayDelivery remote,
        DeliveryRequestOutcome outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        delivery.ConfirmCreated(remote.ProviderDeliveryId, remote.TrackingUrl, remote.Fee, now);

        // The customer pays what was quoted at checkout (or the estimate); the provider's price is the delivery's cost.
        order.MarkDeliveryRequested(delivery.Id, order.DeliveryFee ?? remote.Fee, now);
        await db.SaveChangesAsync(cancellationToken);

        metrics.Requested(outcome == DeliveryRequestOutcome.Requested ? "created" : "adopted");
        LogRequested(order.Id, delivery.Id, remote.ProviderDeliveryId, outcome);
        return Result.Ok(outcome);
    }

    /// <summary>Contract rejections are final (cancel + release stock); anything transient is left for the next run.</summary>
    private async Task<Result<DeliveryRequestOutcome>> FailPermanentlyOrDeferAsync(Order order, Failure failure, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (failure.Kind != FailureKind.Validation)
        {
            metrics.Requested("deferred");
            LogDeferred(order.Id, failure.Code);
            return failure;
        }

        order.Cancel(OrderCancellationReason.DeliveryFailed, now, actor: null, note: failure.Code);
        await StockRelease.ReleaseAsync(db, order, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        metrics.Requested("rejected");
        LogRejected(order.Id, failure.Code);
        return failure;
    }

    [LoggerMessage(EventId = 6010, Level = LogLevel.Information, Message = "Delivery {DeliveryId} for order {OrderId} {Outcome} at provider ({ProviderDeliveryId})")]
    private partial void LogRequested(OrderId orderId, DeliveryId deliveryId, string providerDeliveryId, DeliveryRequestOutcome outcome);

    [LoggerMessage(EventId = 6011, Level = LogLevel.Information, Message = "Order {OrderId} requoted for delivery (fee {Fee})")]
    private partial void LogRequoted(OrderId orderId, decimal fee);

    [LoggerMessage(EventId = 6012, Level = LogLevel.Warning, Message = "Provider rejected the quote for order {OrderId} ({Code}) on attempt {Attempt}")]
    private partial void LogQuoteRejected(OrderId orderId, string code, int attempt);

    [LoggerMessage(EventId = 6013, Level = LogLevel.Warning, Message = "Delivery request for order {OrderId} deferred ({Code})")]
    private partial void LogDeferred(OrderId orderId, string code);

    [LoggerMessage(EventId = 6014, Level = LogLevel.Error, Message = "Delivery for order {OrderId} rejected permanently ({Code}); order cancelled, refund required (BL-244)")]
    private partial void LogRejected(OrderId orderId, string code);

    [LoggerMessage(EventId = 6015, Level = LogLevel.Error, Message = "Delivery for order {OrderId} not created: quote expired twice")]
    private partial void LogGaveUp(OrderId orderId);
}
