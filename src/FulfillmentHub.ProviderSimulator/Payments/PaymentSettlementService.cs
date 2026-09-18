using System.Text.Json;
using FulfillmentHub.ProviderSimulator.Common;
using Microsoft.Extensions.Options;

namespace FulfillmentHub.ProviderSimulator.Payments;

/// <summary>
/// Moves pending payments to paid/failed once their settlement time arrives and emits the
/// <c>payment.status_changed</c> webhook (possibly delayed or duplicated, per configuration).
/// </summary>
public sealed partial class PaymentSettlementService(
    PaymentSimulatorStore store,
    WebhookOutbox webhooks,
    IOptionsMonitor<PaymentSimulatorOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentSettlementService> logger) : BackgroundService
{
    public const string ProviderName = "simulated-psp";
    public const string EventType = "payment.status_changed";

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
                foreach (var payment in store.TakeDueForSettlement())
                {
                    LogSettled(payment.Id, payment.Status);
                    Publish(payment);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private void Publish(SimulatedPayment payment)
    {
        var settings = options.CurrentValue;

        if (!payment.SendWebhooks || string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            return;
        }

        var evt = new PaymentWebhookEvent(
            "evt_" + Guid.CreateVersion7().ToString("N")[..20],
            EventType,
            timeProvider.GetUtcNow(),
            new PaymentWebhookData(payment.Id, payment.Status, payment.FailureCode, payment.OrderReference, payment.UpdatedAt));

        var body = JsonSerializer.Serialize(evt, JsonOptions);
        var delay = TimeSpan.FromMilliseconds(settings.WebhookDelayMs);
        var webhook = new OutgoingWebhook(ProviderName, evt.Id, settings.WebhookUrl, body, settings.WebhookSigningKey, delay);

        webhooks.Enqueue(webhook);

        if (settings.WebhookDuplicateRate > 0 && Random.Shared.NextDouble() < settings.WebhookDuplicateRate)
        {
            webhooks.Enqueue(webhook with { Delay = delay + TimeSpan.FromMilliseconds(50) });
        }
    }

    [LoggerMessage(EventId = 9100, Level = LogLevel.Information, Message = "Payment {PaymentId} settled as {Status}")]
    private partial void LogSettled(string paymentId, string status);
}
