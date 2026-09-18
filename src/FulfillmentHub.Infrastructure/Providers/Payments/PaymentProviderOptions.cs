using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Providers.Payments;

/// <summary>Connection and resilience settings for the (simulated) payment provider. Secrets come from user-secrets/env.</summary>
public sealed class PaymentProviderOptions
{
    public const string SectionName = "Providers:Payment";

    [Required]
    [Url]
    public required string BaseUrl { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string ApiKey { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MinLength(16)]
    public required string WebhookSigningKey { get; init; }

    [Range(1, 120)]
    public int TotalTimeoutSeconds { get; init; } = 15;

    [Range(1, 60)]
    public int AttemptTimeoutSeconds { get; init; } = 5;

    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    [Range(10, 10_000)]
    public int RetryBaseDelayMs { get; init; } = 500;

    /// <summary>Webhooks whose timestamp differs from the server clock by more than this are rejected (replay protection).</summary>
    [Range(30, 3600)]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public TimeSpan TotalTimeout => TimeSpan.FromSeconds(TotalTimeoutSeconds);

    public TimeSpan AttemptTimeout => TimeSpan.FromSeconds(AttemptTimeoutSeconds);

    public TimeSpan RetryBaseDelay => TimeSpan.FromMilliseconds(RetryBaseDelayMs);

    public TimeSpan WebhookTimestampTolerance => TimeSpan.FromSeconds(WebhookTimestampToleranceSeconds);
}
