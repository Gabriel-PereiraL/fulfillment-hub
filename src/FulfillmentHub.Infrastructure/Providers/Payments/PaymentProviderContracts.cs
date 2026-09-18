namespace FulfillmentHub.Infrastructure.Providers.Payments;

// Wire contracts of the simulated provider (docs/INTEGRATIONS.md §3). Serialized as snake_case JSON.

internal sealed record ProviderCreatePaymentRequest(long Amount, string Currency, string OrderReference, string? CustomerReference, bool Capture);

internal sealed record ProviderPaymentResponse(
    string Id,
    string Status,
    long Amount,
    string Currency,
    string OrderReference,
    string? FailureCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

internal sealed record ProviderRefundRequest(long? Amount);

internal sealed record ProviderRefundResponse(string RefundId, string PaymentId, string Status, long Amount, DateTimeOffset CreatedAt);

internal sealed record ProviderErrorResponse(string? Code, string? Message);
