using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FulfillmentHub.Infrastructure.Webhooks;

public enum WebhookSignatureResult
{
    Valid = 0,
    MissingSignature = 1,
    InvalidSignature = 2,
    MissingTimestamp = 3,
    StaleTimestamp = 4,
}

/// <summary>
/// Verifies provider webhooks: HMAC-SHA256 (hex) of the raw body with the shared signing key, compared in constant
/// time, plus a timestamp window against replay (docs/INTEGRATIONS.md §5, SECURITY.md §1).
/// </summary>
public sealed class WebhookSignatureVerifier(TimeProvider timeProvider)
{
    public WebhookSignatureResult Verify(string signingKey, ReadOnlySpan<byte> body, string? signatureHex, string? unixTimestamp, TimeSpan tolerance)
    {
        if (string.IsNullOrWhiteSpace(unixTimestamp)
            || !long.TryParse(unixTimestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return WebhookSignatureResult.MissingTimestamp;
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if ((timeProvider.GetUtcNow() - sentAt).Duration() > tolerance)
        {
            return WebhookSignatureResult.StaleTimestamp;
        }

        if (string.IsNullOrWhiteSpace(signatureHex))
        {
            return WebhookSignatureResult.MissingSignature;
        }

        if (signatureHex.Length != 64 || !signatureHex.All(char.IsAsciiHexDigit))
        {
            return WebhookSignatureResult.InvalidSignature;
        }

        ReadOnlySpan<byte> provided = Convert.FromHexString(signatureHex);
        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), body, expected);

        return CryptographicOperations.FixedTimeEquals(expected, provided)
            ? WebhookSignatureResult.Valid
            : WebhookSignatureResult.InvalidSignature;
    }
}
