using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;

namespace FulfillmentHub.Application.Identity;

/// <summary>The authenticated principal as seen by use cases (resource-level authorization happens here, not in endpoints).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    UserId UserId { get; }

    CustomerId? CustomerId { get; }

    IReadOnlyList<Role> Roles { get; }

    bool IsInRole(Role role);
}
