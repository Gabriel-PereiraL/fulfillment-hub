using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Identity;

/// <summary>
/// Access-token settings. The signing key is a secret: user-secrets locally, Secrets Manager in the cloud — never
/// in appsettings. Validated at startup so a misconfigured host refuses to start.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HS256 needs at least 256 bits of key material.</summary>
    public const int MinimumSigningKeyLength = 32;

    [Required(AllowEmptyStrings = false)]
    public required string Issuer { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string Audience { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumSigningKeyLength)]
    public required string SigningKey { get; init; }

    [Range(1, 60)]
    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    /// <summary>Tolerance for clock differences between issuer and validator.</summary>
    [Range(0, 120)]
    public int ClockSkewSeconds { get; init; } = 30;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenLifetimeMinutes);

    public TimeSpan ClockSkew => TimeSpan.FromSeconds(ClockSkewSeconds);
}
