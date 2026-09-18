using FulfillmentHub.Domain.Common;

namespace FulfillmentHub.Domain.Customers;

/// <summary>A saved address inside the <see cref="Customer"/> aggregate (child entity, plain Guid key).</summary>
public sealed class CustomerAddress
{
    public const int LabelMaxLength = 40;

    private CustomerAddress()
    {
    }

    public Guid Id { get; private set; }

    public string Label { get; private set; } = null!;

    public Address Address { get; private set; } = null!;

    public bool IsDefault { get; private set; }

    internal static CustomerAddress Create(string label, Address address, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > LabelMaxLength)
        {
            throw new DomainException($"Address label is required and must have at most {LabelMaxLength} characters.");
        }

        return new CustomerAddress
        {
            Id = Guid.CreateVersion7(),
            Label = label.Trim(),
            Address = address,
            IsDefault = isDefault,
        };
    }

    internal void MarkAsDefault() => IsDefault = true;

    internal void ClearDefault() => IsDefault = false;
}
