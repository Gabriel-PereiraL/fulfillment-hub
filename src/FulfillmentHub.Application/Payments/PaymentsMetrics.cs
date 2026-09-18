using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Payments;

/// <summary>Payment and webhook metrics (docs/OBSERVABILITY.md §4).</summary>
public sealed class PaymentsMetrics
{
    private readonly Counter<long> _webhooksReceived;
    private readonly Counter<long> _webhooksRejected;
    private readonly Counter<long> _webhooksDuplicates;
    private readonly Counter<long> _reconciliationCorrections;
    private readonly Counter<long> _paymentsSettled;

    public PaymentsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);

        _webhooksReceived = meter.CreateCounter<long>("fh.webhooks.received", "{webhook}", "Webhooks accepted (valid signature).");
        _webhooksRejected = meter.CreateCounter<long>("fh.webhooks.rejected", "{webhook}", "Webhooks rejected (bad signature, stale timestamp, malformed).");
        _webhooksDuplicates = meter.CreateCounter<long>("fh.webhooks.duplicates", "{webhook}", "Webhooks already seen (same provider event id).");
        _reconciliationCorrections = meter.CreateCounter<long>("fh.reconciliation.corrections", "{correction}", "State corrections made by reconciliation jobs.");
        _paymentsSettled = meter.CreateCounter<long>("fh.payments.settled", "{payment}", "Payments that reached a final provider status.");
    }

    public void WebhookReceived(string provider, string eventType) => _webhooksReceived.Add(1, Tag("provider", provider), Tag("event_type", eventType));

    public void WebhookRejected(string provider, string reason) => _webhooksRejected.Add(1, Tag("provider", provider), Tag("reason", reason));

    public void WebhookDuplicate(string provider) => _webhooksDuplicates.Add(1, Tag("provider", provider));

    public void ReconciliationCorrection(string kind) => _reconciliationCorrections.Add(1, Tag("kind", kind));

    public void PaymentSettled(string status) => _paymentsSettled.Add(1, Tag("status", status));

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
