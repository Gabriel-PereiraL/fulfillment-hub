using FulfillmentHub.Domain.Common;
using FulfillmentHub.Domain.Identity;

namespace FulfillmentHub.Domain.Customers;

public sealed class Customer : AggregateRoot<CustomerId>
{
    public const int NameMaxLength = 120;
    public const int MaxAddresses = 5;

    private readonly List<CustomerAddress> _addresses = [];

    private Customer(CustomerId id)
        : base(id)
    {
    }

    public UserId UserId { get; private set; }

    public string Name { get; private set; } = null!;

    public EmailAddress Email { get; private set; } = null!;

    public PhoneNumber Phone { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<CustomerAddress> Addresses => _addresses.AsReadOnly();

    public CustomerAddress? DefaultAddress => _addresses.FirstOrDefault(a => a.IsDefault);

    public static Customer Register(UserId userId, string name, EmailAddress email, PhoneNumber phone, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Customer name is required and must have at most {NameMaxLength} characters.");
        }

        return new Customer(CustomerId.New())
        {
            UserId = userId,
            Name = name.Trim(),
            Email = email,
            Phone = phone,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public CustomerAddress AddAddress(string label, Address address, DateTimeOffset now)
    {
        if (_addresses.Count >= MaxAddresses)
        {
            throw new DomainException($"A customer can have at most {MaxAddresses} addresses.");
        }

        var entry = CustomerAddress.Create(label, address, isDefault: _addresses.Count == 0);
        _addresses.Add(entry);
        UpdatedAt = now;

        return entry;
    }

    public void RemoveAddress(Guid addressId, DateTimeOffset now)
    {
        var entry = FindAddress(addressId);
        _addresses.Remove(entry);

        if (entry.IsDefault && _addresses.Count > 0)
        {
            _addresses[0].MarkAsDefault();
        }

        UpdatedAt = now;
    }

    public void SetDefaultAddress(Guid addressId, DateTimeOffset now)
    {
        var entry = FindAddress(addressId);

        foreach (var address in _addresses)
        {
            address.ClearDefault();
        }

        entry.MarkAsDefault();
        UpdatedAt = now;
    }

    public void Deactivate(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    private CustomerAddress FindAddress(Guid addressId) =>
        _addresses.FirstOrDefault(a => a.Id == addressId)
        ?? throw new DomainException($"Address '{addressId}' does not belong to customer '{Id}'.");
}
