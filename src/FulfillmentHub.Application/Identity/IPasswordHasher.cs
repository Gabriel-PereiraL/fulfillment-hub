namespace FulfillmentHub.Application.Identity;

public enum PasswordVerification
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
}

/// <summary>Port over ASP.NET Core Identity's <c>PasswordHasher&lt;TUser&gt;</c> (implemented in Infrastructure).</summary>
public interface IPasswordHasher
{
    /// <summary>
    /// A valid hash of a random password, computed once. Verifying an attempt against it when no account matches
    /// keeps the failure path as expensive as a real verification, so response time does not reveal whether an
    /// e-mail is registered.
    /// </summary>
    string DecoyHash { get; }

    string Hash(string password);

    PasswordVerification Verify(string passwordHash, string providedPassword);
}
