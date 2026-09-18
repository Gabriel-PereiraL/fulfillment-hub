namespace FulfillmentHub.Application.Common;

public enum FailureKind
{
    Validation = 0,
    NotFound = 1,
    Conflict = 2,
    Unauthorized = 3,
    Forbidden = 4,
    Unavailable = 5,
}

/// <summary>An expected failure of a use case. Unexpected failures are exceptions.</summary>
public sealed record Failure(string Code, string Message, FailureKind Kind)
{
    public static Failure Validation(string code, string message) => new(code, message, FailureKind.Validation);

    public static Failure NotFound(string code, string message) => new(code, message, FailureKind.NotFound);

    public static Failure Conflict(string code, string message) => new(code, message, FailureKind.Conflict);

    public static Failure Unauthorized(string code, string message) => new(code, message, FailureKind.Unauthorized);

    public static Failure Forbidden(string code, string message) => new(code, message, FailureKind.Forbidden);

    /// <summary>A transient failure of a dependency: the same request may succeed later.</summary>
    public static Failure Unavailable(string code, string message) => new(code, message, FailureKind.Unavailable);
}
