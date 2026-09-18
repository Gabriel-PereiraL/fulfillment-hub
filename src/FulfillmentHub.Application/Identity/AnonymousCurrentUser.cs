using FulfillmentHub.Domain.Customers;
using FulfillmentHub.Domain.Identity;

namespace FulfillmentHub.Application.Identity;

/// <summary>The principal of background work: nobody. Use cases that require a user fail closed with it.</summary>
public sealed class AnonymousCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;

    public UserId UserId => default;

    public CustomerId? CustomerId => null;

    public IReadOnlyList<Role> Roles => [];

    public bool IsInRole(Role role) => false;
}
