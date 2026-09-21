using System.Diagnostics.Metrics;
using FulfillmentHub.Application.Common;

namespace FulfillmentHub.Api.Idempotency;

/// <summary><c>fh.idempotency.hits</c> (docs/OBSERVABILITY.md §4): repeated keys by outcome. Singleton.</summary>
public sealed class IdempotencyMetrics
{
    private readonly Counter<long> _hits;

    public IdempotencyMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(ApplicationTelemetry.Name);
        _hits = meter.CreateCounter<long>(
            "fh.idempotency.hits", "{request}", "Requests that reused an Idempotency-Key: replayed, in-progress conflict or payload mismatch.");
    }

    public void Hit(string outcome) => _hits.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
