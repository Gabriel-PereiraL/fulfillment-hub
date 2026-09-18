using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(AllowEmptyStrings = false)]
    public required string ConnectionString { get; init; }

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; init; } = 30;
}
