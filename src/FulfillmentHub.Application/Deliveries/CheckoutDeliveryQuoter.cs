using FulfillmentHub.Application.Common;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Deliveries;
using FulfillmentHub.Domain.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.Application.Deliveries;

/// <summary>The fee the customer will be charged and, when the provider answered, the quote to reuse when the delivery is created.</summary>
public sealed record CheckoutQuote(Money Fee, GatewayQuote? Quote);

/// <summary>
/// Quotes the delivery at checkout (D-51): the customer pays the real fee when the provider answers; when it does not
/// (outage, undeliverable address handled elsewhere), the configured estimate is charged so placing the order never
/// depends on the provider being up.
/// </summary>
public sealed partial class CheckoutDeliveryQuoter(
    IDeliveryProviderClient provider,
    IOptions<FulfillmentOriginOptions> origin,
    DeliveriesMetrics metrics,
    ILogger<CheckoutDeliveryQuoter> logger)
{
    public string ProviderName => provider.ProviderName;

    public async Task<Result<CheckoutQuote>> QuoteAsync(DeliveryParty dropoff, CancellationToken cancellationToken)
    {
        var result = await provider.QuoteAsync(new GatewayQuoteRequest(origin.Value.ToParty(), dropoff, ManifestTotalValue: null), cancellationToken);

        if (result.IsSuccess)
        {
            metrics.Quote("quoted");
            return Result.Ok(new CheckoutQuote(result.Value.Fee, result.Value));
        }

        if (result.Failure.Kind == FailureKind.Validation)
        {
            // The provider will not serve this address (e.g. address_undeliverable): the order must not be placed.
            metrics.Quote("rejected");
            return Failure.Validation("order.address_undeliverable", "The delivery address cannot be served.");
        }

        metrics.Quote("fallback_fee");
        LogFallbackFee(result.Failure.Code);
        return Result.Ok(new CheckoutQuote(Money.Of(origin.Value.EstimatedDeliveryFee), Quote: null));
    }

    /// <summary>Persists a provider quote for the order so the delivery request can reuse it while it is valid.</summary>
    public static DeliveryQuote ToAggregate(OrderId orderId, string providerName, GatewayQuote quote, DateTimeOffset now) =>
        DeliveryQuote.Create(orderId, providerName, quote.ProviderQuoteId, quote.Fee, quote.EstimatedDropoffAt, quote.DurationMinutes, quote.PickupDurationMinutes, quote.ExpiresAt, now);

    [LoggerMessage(EventId = 6000, Level = LogLevel.Warning, Message = "Delivery provider could not quote at checkout ({Code}); charging the estimated fee")]
    private partial void LogFallbackFee(string code);
}
