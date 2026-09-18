using FulfillmentHub.Application.Webhooks;

namespace FulfillmentHub.Infrastructure.Webhooks;

/// <summary>Provider event id and type, extracted before the event is recorded (null = malformed payload → 400).</summary>
public sealed record WebhookIdentity(string EventId, string EventType);

/// <summary>
/// Provider-specific half of webhook handling, keyed by provider name in DI: identifies a raw payload and later
/// processes the stored event. Runs in the API request (messaging off) or in the Worker's queue consumer (messaging on).
/// </summary>
public interface IWebhookProcessor
{
    WebhookIdentity? Identify(string payload);

    Task<WebhookOutcome> ProcessAsync(WebhookEvent webhookEvent, CancellationToken cancellationToken);
}
