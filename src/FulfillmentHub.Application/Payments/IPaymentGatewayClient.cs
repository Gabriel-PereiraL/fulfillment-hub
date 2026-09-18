using FulfillmentHub.Application.Common;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Payments;

namespace FulfillmentHub.Application.Payments;

public sealed record GatewayCreatePayment(string IdempotencyKey, Money Amount, string OrderReference, string? CustomerReference);

/// <summary>What the provider knows about a payment. Statuses are already translated to the domain's.</summary>
public sealed record GatewayPayment(string ProviderPaymentId, PaymentStatus Status, string? FailureCode, DateTimeOffset UpdatedAt);

public sealed record GatewayRefund(string RefundId, PaymentStatus PaymentStatus);

/// <summary>
/// Port to the payment provider (implemented over HTTP with a resilience pipeline in Infrastructure).
/// Failures are expressed as <see cref="Failure"/>: <see cref="FailureKind.Unavailable"/> means "transient —
/// try again later"; every other kind is permanent for this request.
/// </summary>
public interface IPaymentGatewayClient
{
    string ProviderName { get; }

    Task<Result<GatewayPayment>> CreateAsync(GatewayCreatePayment request, CancellationToken cancellationToken);

    Task<Result<GatewayPayment>> GetAsync(string providerPaymentId, CancellationToken cancellationToken);

    Task<Result<GatewayRefund>> RefundAsync(string providerPaymentId, string idempotencyKey, CancellationToken cancellationToken);
}
