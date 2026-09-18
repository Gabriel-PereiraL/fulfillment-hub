namespace FulfillmentHub.Domain.Common;

/// <summary>Phone number in E.164 format: '+' followed by 8 to 15 digits.</summary>
public sealed record PhoneNumber
{
    public const int MaxLength = 16;

    public string Value { get; }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public static PhoneNumber Of(string value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        if (digits.Length is < 8 or > 15)
        {
            throw new DomainException($"'{value}' is not a valid E.164 phone number.");
        }

        return new PhoneNumber("+" + digits);
    }

    /// <summary>All but the last four digits masked, for logs and operator screens.</summary>
    public string Masked => string.Concat(Value[..3], new string('*', Value.Length - 7), Value[^4..]);

    public override string ToString() => Value;
}
