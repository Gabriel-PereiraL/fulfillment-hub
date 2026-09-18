using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Application.Payments;

public sealed class PaymentsMetrics
{
    private readonly Counter<long> _reconciliationCorrections;
    private readonly Counter<long> _paymentsSettled;

    public PaymentsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);
        _reconciliationCorrections = meter.CreateCounter<long>("fh.reconciliation.corrections", "{correction}", "State corrections made by reconciliation jobs.");
        _paymentsSettled = meter.CreateCounter<long>("fh.payments.settled", "{payment}", "Payments that reached a final provider status.");
    }

    public void ReconciliationCorrection(string kind) => _reconciliationCorrections.Add(1, Tag("kind", kind));

    public void PaymentSettled(string status) => _paymentsSettled.Add(1, Tag("status", status));

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);
}
