using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Deliveries;

/// <summary>
/// In-memory state of the simulated delivery provider: quotes, deliveries, idempotency keys and the timed lifecycle.
/// Thread-safe; state is lost on restart by design (D-43).
/// </summary>
public sealed class DeliverySimulatorStore(IOptionsMonitor<DeliverySimulatorOptions> options, TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions AddressJson = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static readonly string[] CourierNames = ["Ana", "Bruno", "Carla", "Diego", "Elisa", "Fábio"];
    private static readonly string[] Vehicles = ["bicycle", "motorcycle", "car"];

    private readonly ConcurrentDictionary<string, SimulatedQuote> _quotes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SimulatedDelivery> _deliveries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (string DeliveryId, DateTimeOffset ExpiresAt)> _idempotency = new(StringComparer.Ordinal);

    public enum QuoteOutcome
    {
        Created,
        InvalidAddress,
        AddressUndeliverable,
    }

    public enum CreateOutcome
    {
        Created,
        InvalidAddress,
        AddressUndeliverable,
        QuoteNotFound,
        ExpiredQuote,
        UsedQuote,
        DuplicateDelivery,
    }

    public enum CancelOutcome
    {
        Cancelled,
        NotFound,
        Noncancelable,
    }

    public (QuoteOutcome Outcome, SimulatedQuote? Quote) Quote(DeliveryQuoteRequest request)
    {
        if (!TryParseAddress(request.PickupAddress, out _) || !TryParseAddress(request.DropoffAddress, out var dropoff))
        {
            return (QuoteOutcome.InvalidAddress, null);
        }

        if (IsUndeliverable(dropoff.ZipCode))
        {
            return (QuoteOutcome.AddressUndeliverable, null);
        }

        var now = timeProvider.GetUtcNow();
        var settings = options.CurrentValue;
        var ttl = HasSandboxSuffix(dropoff.ZipCode, "001") ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(settings.QuoteTtlSeconds);
        // Deterministic per zip (string hashes are randomized per process). Fees are whole reais so the payment
        // simulator's sandbox amounts (cents …99/…98 on the order total) keep working with the fee included.
        var distanceFactor = dropoff.ZipCode.Where(char.IsAsciiDigit).Sum(c => c - '0') % 8;

        var quote = new SimulatedQuote
        {
            Id = "dqt_" + Guid.CreateVersion7().ToString("N")[..20],
            Created = now,
            Expires = now + ttl,
            Fee = settings.BaseFeeCents + (distanceFactor * 100),
            DropoffZip = dropoff.ZipCode,
            DurationMinutes = 20 + (distanceFactor * 5),
            PickupDurationMinutes = 10 + distanceFactor,
        };

        _quotes[quote.Id] = quote;
        return (QuoteOutcome.Created, quote);
    }

    public (CreateOutcome Outcome, SimulatedDelivery? Delivery) Create(CreateDeliveryRequest request)
    {
        var now = timeProvider.GetUtcNow();
        var settings = options.CurrentValue;

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey)
            && _idempotency.TryGetValue(request.IdempotencyKey, out var existing)
            && existing.ExpiresAt > now)
        {
            return (CreateOutcome.DuplicateDelivery, _deliveries[existing.DeliveryId]);
        }

        if (!string.IsNullOrWhiteSpace(request.ExternalId))
        {
            var active = _deliveries.Values.FirstOrDefault(d => d.ExternalId == request.ExternalId && !d.Complete);
            if (active is not null)
            {
                return (CreateOutcome.DuplicateDelivery, active);
            }
        }

        if (!TryParseAddress(request.PickupAddress, out _) || !TryParseAddress(request.DropoffAddress, out var dropoff))
        {
            return (CreateOutcome.InvalidAddress, null);
        }

        if (IsUndeliverable(dropoff.ZipCode))
        {
            return (CreateOutcome.AddressUndeliverable, null);
        }

        SimulatedQuote? quote = null;
        if (request.QuoteId is not null)
        {
            if (!_quotes.TryGetValue(request.QuoteId, out quote))
            {
                return (CreateOutcome.QuoteNotFound, null);
            }

            lock (quote)
            {
                if (quote.Expires <= now)
                {
                    return (CreateOutcome.ExpiredQuote, null);
                }

                if (quote.Used)
                {
                    return (CreateOutcome.UsedQuote, null);
                }

                quote.Used = true;
            }
        }

        var fee = quote?.Fee ?? settings.BaseFeeCents;
        var delivery = new SimulatedDelivery
        {
            Id = "del_" + Guid.CreateVersion7().ToString("N")[..20],
            Uuid = Guid.CreateVersion7().ToString(),
            QuoteId = quote?.Id,
            Fee = fee,
            Created = now,
            Updated = now,
            ExternalId = request.ExternalId,
            ManifestReference = request.ManifestReference,
            DropoffZip = dropoff.ZipCode,
            WillReturn = HasSandboxSuffix(dropoff.ZipCode, "002"),
            NextTransitionAt = now.AddMilliseconds(settings.CourierAssignMs),
            PickupEta = now.AddMinutes(quote?.PickupDurationMinutes ?? 10),
            DropoffEta = now.AddMinutes(quote?.DurationMinutes ?? 30),
        };

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var slot = _idempotency.GetOrAdd(request.IdempotencyKey, _ => (delivery.Id, now.AddMinutes(settings.IdempotencyTtlMinutes)));
            if (slot.DeliveryId != delivery.Id)
            {
                return (CreateOutcome.DuplicateDelivery, _deliveries[slot.DeliveryId]);
            }
        }

        _deliveries[delivery.Id] = delivery;
        return (CreateOutcome.Created, delivery);
    }

    public SimulatedDelivery? Find(string id) => _deliveries.GetValueOrDefault(id);

    public (CancelOutcome Outcome, SimulatedDelivery? Delivery) Cancel(string id)
    {
        if (!_deliveries.TryGetValue(id, out var delivery))
        {
            return (CancelOutcome.NotFound, null);
        }

        lock (delivery)
        {
            if (delivery.Status == "canceled")
            {
                return (CancelOutcome.Cancelled, delivery);
            }

            if (delivery.Status is not ("pending" or "pickup"))
            {
                return (CancelOutcome.Noncancelable, delivery);
            }

            delivery.Status = "canceled";
            delivery.NextTransitionAt = null;
            delivery.Updated = timeProvider.GetUtcNow();
            return (CancelOutcome.Cancelled, delivery);
        }
    }

    /// <summary>Advances every delivery whose next transition is due and returns the ones that changed.</summary>
    public IReadOnlyList<SimulatedDelivery> AdvanceDue()
    {
        var now = timeProvider.GetUtcNow();
        var step = TimeSpan.FromMilliseconds(options.CurrentValue.StepMs);
        var changed = new List<SimulatedDelivery>();

        foreach (var delivery in _deliveries.Values)
        {
            lock (delivery)
            {
                if (delivery.Complete || delivery.NextTransitionAt is not { } due || due > now)
                {
                    continue;
                }

                var index = Array.IndexOf(SimulatedDelivery.Lifecycle, delivery.Status);
                var next = SimulatedDelivery.Lifecycle[index + 1];

                if (next == "delivered" && delivery.WillReturn)
                {
                    next = "returned";
                    delivery.UndeliverableReason = "customer_unavailable";
                }

                delivery.Status = next;
                delivery.Updated = now;
                delivery.NextTransitionAt = delivery.Complete ? null : now + step;

                if (next == "pickup")
                {
                    delivery.Courier = AssignCourier(delivery.Id);
                }

                changed.Add(delivery);
            }
        }

        return changed;
    }

    /// <summary>Sandbox rule: dropoff zip codes starting with 00000 cannot be served.</summary>
    public static bool IsUndeliverable(string zipCode) => zipCode.StartsWith("00000", StringComparison.Ordinal);

    /// <summary>Sandbox rule keyed on the last three digits of the zip code (e.g. <c>50000-001</c>).</summary>
    public static bool HasSandboxSuffix(string zipCode, string suffix)
    {
        var digits = new string(zipCode.Where(char.IsAsciiDigit).ToArray());
        return digits.EndsWith(suffix, StringComparison.Ordinal);
    }

    private static bool TryParseAddress(string json, out StructuredAddress address)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<StructuredAddress>(json, AddressJson);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ZipCode) || parsed.StreetAddress is not { Length: > 0 })
            {
                address = null!;
                return false;
            }

            address = parsed;
            return true;
        }
        catch (JsonException)
        {
            address = null!;
            return false;
        }
    }

    private static CourierResponse AssignCourier(string deliveryId)
    {
        var seed = deliveryId.Sum(c => c); // stable across processes, unlike string hash codes
        return new CourierResponse(
            CourierNames[seed % CourierNames.Length],
            4.5 + ((seed % 5) / 10.0),
            Vehicles[seed % Vehicles.Length],
            "+55819" + (10000000 + (seed % 89999999)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            new LatLng(-8.05 + ((seed % 100) / 1000.0), -34.9 + ((seed % 70) / 1000.0)),
            null);
    }
}
