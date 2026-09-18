namespace FulfillmentHub.Domain.Common;

/// <summary>
/// Raised when a domain invariant is violated. Represents misuse of the domain model
/// (an impossible state), not an expected business outcome.
/// </summary>
public class DomainException : Exception
{
    public DomainException()
    {
    }

    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
