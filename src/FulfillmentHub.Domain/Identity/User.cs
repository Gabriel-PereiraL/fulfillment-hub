using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Identity;

/// <summary>
/// Application user. Password hashing lives in Infrastructure (ASP.NET Core <c>PasswordHasher</c>); the domain only
/// stores the resulting hash. Roles are a fixed set (<see cref="Role"/>) persisted as a text array.
/// </summary>
public sealed class User : AggregateRoot<UserId>
{
    public const int PasswordHashMaxLength = 512;

    private readonly List<Role> _roles = [];

    private User(UserId id)
        : base(id)
    {
    }

    public EmailAddress Email { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public IReadOnlyList<Role> Roles => _roles.AsReadOnly();

    public static User Create(EmailAddress email, string passwordHash, IEnumerable<Role> roles, DateTimeOffset now)
    {
        var user = new User(UserId.New())
        {
            Email = email,
            IsActive = true,
            CreatedAt = now,
        };

        user.SetPasswordHash(passwordHash);

        foreach (var role in roles.Distinct())
        {
            user._roles.Add(role);
        }

        if (user._roles.Count == 0)
        {
            throw new DomainException("A user must have at least one role.");
        }

        return user;
    }

    public bool HasRole(Role role) => _roles.Contains(role);

    public void GrantRole(Role role)
    {
        if (!_roles.Contains(role))
        {
            _roles.Add(role);
        }
    }

    public void RevokeRole(Role role)
    {
        if (_roles.Count == 1 && _roles.Contains(role))
        {
            throw new DomainException("A user must keep at least one role.");
        }

        _roles.Remove(role);
    }

    public void SetPasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > PasswordHashMaxLength)
        {
            throw new DomainException("Password hash is required.");
        }

        PasswordHash = passwordHash;
    }

    public void RecordLogin(DateTimeOffset now) => LastLoginAt = now;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
