using FulfillmentHub.Application.Common.Persistence;
using FulfillmentHub.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace FulfillmentHub.Application.Identity;

/// <summary>Read-side queries over users (administration). Projections only, no tracking.</summary>
public sealed class UserQueries(IFulfillmentHubDbContext db)
{
    public async Task<UserDetails?> GetByIdAsync(UserId id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

        return user is null
            ? null
            : new UserDetails(
                user.Id.Value,
                user.Email.Value,
                user.Roles.Select(r => r.ToString()).ToList(),
                user.IsActive,
                user.CreatedAt,
                user.LastLoginAt);
    }
}
