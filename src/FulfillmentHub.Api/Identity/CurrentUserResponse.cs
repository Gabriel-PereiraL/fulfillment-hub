namespace FulfillmentHub.Api.Identity;

public sealed record CurrentUserResponse(Guid UserId, Guid? CustomerId, IReadOnlyList<string> Roles);
