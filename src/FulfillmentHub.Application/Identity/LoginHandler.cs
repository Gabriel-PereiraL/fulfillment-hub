using FulfillmentHub.Application.Common;
using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Application.Identity;

/// <summary>
/// Authenticates a user by e-mail and password and issues an access token.
/// Every failure (unknown e-mail, wrong password, inactive account) produces the same error and the same amount
/// of work — the password hash is always verified — so the response neither reveals whether an account exists
/// nor leaks a timing difference (OWASP A07). The reason is logged (without personal data) for monitoring.
/// </summary>
public sealed partial class LoginHandler(
    IFulfillmentHubDbContext db,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    TimeProvider timeProvider,
    ILogger<LoginHandler> logger)
{
    private static readonly Failure InvalidCredentials =
        Failure.Unauthorized("auth.invalid_credentials", "Invalid e-mail or password.");

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        EmailAddress email;
        try
        {
            email = EmailAddress.Of(command.Email);
        }
        catch (DomainException)
        {
            passwordHasher.Verify(passwordHasher.DecoyHash, command.Password);
            LogLoginFailed(LoginFailureReason.MalformedEmail);
            return InvalidCredentials;
        }

        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            passwordHasher.Verify(passwordHasher.DecoyHash, command.Password);
            LogLoginFailed(LoginFailureReason.UnknownUser);
            return InvalidCredentials;
        }

        var verification = passwordHasher.Verify(user.PasswordHash, command.Password);

        if (verification == PasswordVerification.Failed)
        {
            LogLoginFailed(LoginFailureReason.WrongPassword);
            return InvalidCredentials;
        }

        if (!user.IsActive)
        {
            LogLoginFailed(LoginFailureReason.InactiveUser);
            return InvalidCredentials;
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwordHasher.Hash(command.Password));
        }

        var customerId = await db.Customers
            .Where(c => c.UserId == user.Id)
            .Select(c => (CustomerId?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);

        user.RecordLogin(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var token = tokenIssuer.Issue(user, customerId);
        LogLoginSucceeded(user.Id);

        return Result.Ok(new LoginResult(token.Token, "Bearer", token.ExpiresAt));
    }

    private enum LoginFailureReason
    {
        MalformedEmail,
        UnknownUser,
        WrongPassword,
        InactiveUser,
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Login succeeded for user {UserId}")]
    private partial void LogLoginSucceeded(UserId userId);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Login failed ({Reason})")]
    private partial void LogLoginFailed(LoginFailureReason reason);
}
