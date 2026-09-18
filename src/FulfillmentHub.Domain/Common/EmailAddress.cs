using System.Net.Mail;

namespace FulfillmentHub.Domain.Common;

/// <summary>Normalized (trimmed, lower-case) e-mail address.</summary>
public sealed record EmailAddress
{
    public const int MaxLength = 254;

    public string Value { get; }

    private EmailAddress(string value)
    {
        Value = value;
    }

    public static EmailAddress Of(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;

        if (normalized.Length is 0 or > MaxLength || !MailAddress.TryCreate(normalized, out _))
        {
            throw new DomainException($"'{value}' is not a valid e-mail address.");
        }

        return new EmailAddress(normalized);
    }

    public override string ToString() => Value;
}
