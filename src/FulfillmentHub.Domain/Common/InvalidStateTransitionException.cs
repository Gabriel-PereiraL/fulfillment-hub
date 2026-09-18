namespace FulfillmentHub.Domain.Common;

public sealed class InvalidStateTransitionException(string entity, string from, string to)
    : DomainException($"{entity} cannot transition from '{from}' to '{to}'.")
{
    public string Entity { get; } = entity;

    public string From { get; } = from;

    public string To { get; } = to;
}
