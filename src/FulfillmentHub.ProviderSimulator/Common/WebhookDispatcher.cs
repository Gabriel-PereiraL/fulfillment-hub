using System.Security.Cryptography;
using System.Text;

namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>
/// Delivers signed webhooks the way real providers do (docs/INTEGRATIONS.md §1.5): HMAC-SHA256 of the raw body in a
/// header, a timestamp, retries with exponential backoff when the receiver does not answer 2xx, and no ordering
/// guarantee. Deliveries are best-effort: after the last retry the event is logged and dropped.
/// </summary>
public sealed partial class WebhookDispatcher(
    WebhookOutbox queue,
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    ILogger<WebhookDispatcher> logger) : BackgroundService
{
    public const string HttpClientName = "webhooks";
    public const string SignatureHeader = "X-Signature";
    public const string TimestampHeader = "X-Timestamp";
    public const string EventIdHeader = "X-Event-Id";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var webhook in queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Deliveries run concurrently so one slow receiver does not delay the others (and ordering is not guaranteed).
                _ = DeliverAsync(webhook, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task DeliverAsync(OutgoingWebhook webhook, CancellationToken cancellationToken)
    {
        try
        {
            if (webhook.Delay > TimeSpan.Zero)
            {
                await Task.Delay(webhook.Delay, timeProvider, cancellationToken);
            }

            for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
            {
                var status = await SendOnceAsync(webhook, cancellationToken);

                if (status is >= 200 and < 300)
                {
                    LogDelivered(webhook.Provider, webhook.EventId, attempt + 1);
                    return;
                }

                LogAttemptFailed(webhook.Provider, webhook.EventId, attempt + 1, status);

                if (attempt < RetryDelays.Length)
                {
                    await Task.Delay(RetryDelays[attempt], timeProvider, cancellationToken);
                }
            }

            LogGivenUp(webhook.Provider, webhook.EventId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task<int> SendOnceAsync(OutgoingWebhook webhook, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
        {
            Content = new StringContent(webhook.Body, Encoding.UTF8, "application/json"),
        };

        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        request.Headers.Add(SignatureHeader, Sign(webhook.SigningKey, webhook.Body));
        request.Headers.Add(TimestampHeader, timestamp);
        request.Headers.Add(EventIdHeader, webhook.EventId);

        try
        {
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            return (int)response.StatusCode;
        }
        catch (HttpRequestException)
        {
            return 0;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return 0; // receiver timeout
        }
    }

    public static string Sign(string signingKey, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(body)));

    [LoggerMessage(EventId = 9000, Level = LogLevel.Information, Message = "Webhook {Provider}/{EventId} delivered on attempt {Attempt}")]
    private partial void LogDelivered(string provider, string eventId, int attempt);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Warning, Message = "Webhook {Provider}/{EventId} attempt {Attempt} failed with status {Status}")]
    private partial void LogAttemptFailed(string provider, string eventId, int attempt, int status);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Error, Message = "Webhook {Provider}/{EventId} dropped after retries")]
    private partial void LogGivenUp(string provider, string eventId);
}
