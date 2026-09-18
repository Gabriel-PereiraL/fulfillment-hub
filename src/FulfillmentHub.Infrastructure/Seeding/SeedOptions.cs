using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Seeding;

/// <summary>Passwords for the fictional development users. Provided via user-secrets; required only when seeding.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public const int MinimumPasswordLength = 12;

    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumPasswordLength)]
    public required string AdminPassword { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumPasswordLength)]
    public required string OperatorPassword { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MinLength(MinimumPasswordLength)]
    public required string CustomerPassword { get; init; }
}
