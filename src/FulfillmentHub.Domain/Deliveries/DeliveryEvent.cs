namespace FulfillmentHub.Domain.Deliveries;

/// <summary>A provider status event received for a delivery, whether or not it was applied.</summary>
public sealed class DeliveryEvent
{
    public const int ProviderEventIdMaxLength = 128;
    public const int ProviderStatusMaxLength = 40;

    private DeliveryEvent()
    {
    }

    public Guid Id { get; private set; }

    public string ProviderEventId { get; private set; } = null!;

    public string ProviderStatus { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public DeliveryEventDisposition Disposition { get; private set; }

    internal static DeliveryEvent Record(
        string providerEventId,
        string providerStatus,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        DeliveryEventDisposition disposition) => new()
        {
            Id = Guid.CreateVersion7(),
            ProviderEventId = providerEventId,
            ProviderStatus = providerStatus,
            OccurredAt = occurredAt,
            ReceivedAt = receivedAt,
            Disposition = disposition,
        };
}
