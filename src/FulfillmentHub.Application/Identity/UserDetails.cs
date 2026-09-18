namespace FulfillmentHub.Application.Identity;

public sealed record UserDetails(
    Guid Id,
    string Email,
    IReadOnlyList<string> Roles,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
