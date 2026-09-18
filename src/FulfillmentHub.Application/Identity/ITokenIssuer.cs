using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;

namespace FulfillmentHub.Application.Identity;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues signed access tokens for authenticated users (implemented with JWT in Infrastructure).</summary>
public interface ITokenIssuer
{
    AccessToken Issue(User user, CustomerId? customerId);
}
