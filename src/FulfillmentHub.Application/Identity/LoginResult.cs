namespace FulfillmentHub.Application.Identity;

public sealed record LoginResult(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);
