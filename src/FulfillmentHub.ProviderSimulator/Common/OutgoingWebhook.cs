namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>A webhook waiting to be delivered by <see cref="WebhookDispatcher"/>.</summary>
public sealed record OutgoingWebhook(string Provider, string EventId, string Url, string Body, string SigningKey, TimeSpan Delay);
