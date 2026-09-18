using System.Text.Json;
using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Deliveries;

/// <summary>
/// Moves deliveries through <c>pending → pickup → pickup_complete → dropoff → delivered</c> (or <c>returned</c>) on a
/// timer and emits one <c>event.delivery_status</c> webhook per transition — possibly duplicated, delayed or out of
/// order, per configuration, like the real provider warns.
/// </summary>
public sealed partial class DeliveryLifecycleService(
    DeliverySimulatorStore store,
    WebhookOutbox webhooks,
    IOptionsMonitor<DeliverySimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<DeliveryLifecycleService> logger) : BackgroundService
{
    public const string ProviderName = "uber-like-simulator";
    public const string EventKind = "event.delivery_status";
    public const string SignatureHeader = "X-Uber-Signature";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var delivery in store.AdvanceDue())
                {
                    LogTransition(delivery.Id, delivery.Status);
                    Publish(delivery);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private void Publish(SimulatedDelivery delivery)
    {
        var settings = options.CurrentValue;

        if (!delivery.SendWebhooks || string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }

        DeliveryResponse snapshot;
        lock (delivery)
        {
            snapshot = delivery.ToResponse();
        }

        var evt = new DeliveryStatusEvent(
            "evt_" + Guid.CreateVersion7().ToString("N")[..20],
            EventKind,
            timeProvider.GetUtcNow(),
            snapshot.Status,
            snapshot.Id,
            settings.CustomerId,
            LiveMode: false,
            snapshot);

        var body = JsonSerializer.Serialize(evt, JsonOptions);
        var delay = TimeSpan.FromMilliseconds(settings.WebhookDelayMs);

        if (settings.WebhookOutOfOrder)
        {
            // Consecutive events get random extra delays so they overtake each other at the receiver.
            delay += TimeSpan.FromMilliseconds(Random.Shared.Next(0, 400));
        }

        var webhook = new OutgoingWebhook(ProviderName, evt.Id, settings.WebhookUrl, body, settings.WebhookSigningKey, delay)
        {
            SignatureHeader = SignatureHeader,
        };

        webhooks.Enqueue(webhook);

        if (settings.WebhookDuplicateRate > 0 && Random.Shared.NextDouble() < settings.WebhookDuplicateRate)
        {
            webhooks.Enqueue(webhook with { Delay = delay + TimeSpan.FromMilliseconds(50) });
        }
    }

    [LoggerMessage(EventId = 9200, Level = LogLevel.Information, Message = "Delivery {DeliveryId} is now {Status}")]
    private partial void LogTransition(string deliveryId, string status);
}
