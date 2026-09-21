using System.Diagnostics;

namespace FulfillmentHub.Application.Common;

/// <summary>Meter/ActivitySource name shared by application code and the host's OpenTelemetry registration.</summary>
public static class ApplicationTelemetry
{
    public const string Name = "FulfillmentHub";

    /// <summary>
    /// Source of the use-case spans (`PlaceOrder`, `CancelOrder`, `CreatePayment`, `RequestDelivery`,
    /// `ApplyPaymentWebhook`, `ApplyDeliveryWebhook` — docs/OBSERVABILITY.md §5). Same name as the Infrastructure
    /// spans, so one <c>AddSource</c> covers both.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(Name);
}
