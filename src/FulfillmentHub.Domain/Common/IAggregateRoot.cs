namespace FulfillmentHub.Domain.Common;

/// <summary>Non-generic view of an aggregate root, so infrastructure can drain domain events without knowing the id type.</summary>
public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
