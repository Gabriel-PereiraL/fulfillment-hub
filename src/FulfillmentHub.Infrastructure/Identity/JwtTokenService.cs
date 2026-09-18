using System.Text;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FulfillmentHub.Infrastructure.Identity;

/// <summary>
/// Issues HS256 JWTs with the minimum claims the API needs: subject, roles and (for customers) the customer id.
/// No e-mail or name in the token — the API loads what it needs by id.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenIssuer
{
    private static readonly JsonWebTokenHandler TokenHandler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly JwtOptions _jwt = options.Value;

    public AccessToken Issue(User user, CustomerId? customerId)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(_jwt.AccessTokenLifetime);

        var claims = new Dictionary<string, object>
        {
            [JwtClaimNames.Subject] = user.Id.Value.ToString(),
            [JwtClaimNames.Role] = user.Roles.Select(r => r.ToString()).ToArray(),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
        };

        if (customerId is { } id)
        {
            claims[JwtClaimNames.CustomerId] = id.Value.ToString();
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(CreateSigningKey(_jwt), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(TokenHandler.CreateToken(descriptor), expiresAt);
    }

    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options) =>
        new(Encoding.UTF8.GetBytes(options.SigningKey));
}
