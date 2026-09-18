namespace FulfillmentHub.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
