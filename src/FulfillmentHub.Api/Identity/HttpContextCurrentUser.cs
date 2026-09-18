using System.Security.Claims;
using FulfillmentHub.Application.Identity;
using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;
using FulfillmentHub.Infrastructure.Identity;

namespace FulfillmentHub.Api.Identity;

/// <summary>Reads the authenticated principal from the current request once; use cases depend only on <see cref="ICurrentUser"/>.</summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    public HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        var principal = httpContextAccessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            Roles = [];
            return;
        }

        IsAuthenticated = true;
        UserId = UserId.From(Guid.Parse(principal.FindFirstValue(JwtClaimNames.Subject)
            ?? throw new InvalidOperationException("Authenticated principal has no subject claim.")));

        CustomerId = Guid.TryParse(principal.FindFirstValue(JwtClaimNames.CustomerId), out var customerId)
            ? Domain.Customers.CustomerId.From(customerId)
            : null;

        Roles = principal.FindAll(JwtClaimNames.Role)
            .Select(c => Enum.TryParse<Role>(c.Value, ignoreCase: false, out var role) ? role : (Role?)null)
            .OfType<Role>()
            .Distinct()
            .ToList();
    }

    public bool IsAuthenticated { get; }

    public UserId UserId { get; }

    public CustomerId? CustomerId { get; }

    public IReadOnlyList<Role> Roles { get; }

    public bool IsInRole(Role role) => Roles.Contains(role);
}
