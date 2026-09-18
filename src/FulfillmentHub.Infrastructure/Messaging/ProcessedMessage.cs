namespace FulfillmentHub.Infrastructure.Messaging;

/// <summary>
/// Deduplication record of an at-least-once consumer (ADR-005/ADR-010): inserted in the same transaction as the
/// message's effect, so a redelivered message finds it and is acknowledged without a second effect.
/// </summary>
public sealed class ProcessedMessage
{
    public const int ConsumerMaxLength = 64;
    public const int MessageIdMaxLength = 128;

    private ProcessedMessage()
    {
    }

    public string Consumer { get; private set; } = null!;

    public string MessageId { get; private set; } = null!;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static ProcessedMessage Create(string consumer, string messageId, DateTimeOffset now) => new()
    {
        Consumer = consumer,
        MessageId = messageId,
        ProcessedAt = now,
    };
}
