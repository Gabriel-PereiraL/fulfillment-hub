namespace FulfillmentHub.Infrastructure.Webhooks;

public enum WebhookEventStatus
{
    Received = 0,
    Processed = 1,
    Failed = 2,
    Ignored = 3,
}

/// <summary>
/// A provider event as received (docs/DOMAIN.md §9). The unique (provider, provider_event_id) pair is the
/// deduplication guarantee; the raw payload is kept for reprocessing and audit. Infrastructure record.
/// </summary>
public sealed class WebhookEvent
{
    public const int ProviderMaxLength = 40;
    public const int ProviderEventIdMaxLength = 128;
    public const int EventTypeMaxLength = 64;
    public const int LastErrorMaxLength = 500;

    private WebhookEvent()
    {
    }

    public Guid Id { get; private set; }

    public string Provider { get; private set; } = null!;

    public string ProviderEventId { get; private set; } = null!;

    public string EventType { get; private set; } = null!;

    public string Payload { get; private set; } = null!;

    public DateTimeOffset ReceivedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public WebhookEventStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public string? CorrelationId { get; private set; }

    public static WebhookEvent Receive(string provider, string providerEventId, string eventType, string payload, string? correlationId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        Provider = provider,
        ProviderEventId = providerEventId,
        EventType = eventType,
        Payload = payload,
        ReceivedAt = now,
        Status = WebhookEventStatus.Received,
        CorrelationId = correlationId,
    };

    public void MarkProcessed(DateTimeOffset now)
    {
        Attempts++;
        Status = WebhookEventStatus.Processed;
        ProcessedAt = now;
        LastError = null;
    }

    public void MarkIgnored(string reason, DateTimeOffset now)
    {
        Attempts++;
        Status = WebhookEventStatus.Ignored;
        ProcessedAt = now;
        LastError = Truncate(reason);
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Attempts++;
        Status = WebhookEventStatus.Failed;
        ProcessedAt = now;
        LastError = Truncate(error);
    }

    private static string Truncate(string value) => value.Length <= LastErrorMaxLength ? value : value[..LastErrorMaxLength];
}
