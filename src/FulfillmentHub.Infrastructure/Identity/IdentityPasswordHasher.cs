using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace FulfillmentHub.Infrastructure.Identity;

/// <summary>
/// Adapter over ASP.NET Core Identity's <see cref="PasswordHasher{TUser}"/> (PBKDF2-HMAC-SHA512 with a per-password
/// salt, versioned format). Only the hasher is used — not the rest of the Identity framework (ADR-008).
/// </summary>
public sealed class IdentityPasswordHasher : IPasswordHasher
{
    private static readonly User HasherContext = null!; // PasswordHasher<TUser> never dereferences the user.

    private readonly PasswordHasher<User> _hasher = new();

    public IdentityPasswordHasher()
    {
        DecoyHash = Hash(Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"));
    }

    public string DecoyHash { get; }

    public string Hash(string password) => _hasher.HashPassword(HasherContext, password);

    public PasswordVerification Verify(string passwordHash, string providedPassword)
    {
        PasswordVerificationResult result;
        try
        {
            result = _hasher.VerifyHashedPassword(HasherContext, passwordHash, providedPassword);
        }
        catch (FormatException)
        {
            // A malformed stored hash must behave like a wrong password, never like a server error.
            return PasswordVerification.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed,
        };
    }
}
