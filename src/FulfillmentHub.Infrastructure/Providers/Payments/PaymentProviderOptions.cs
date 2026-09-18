using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Providers.Payments;

/// <summary>Connection and resilience settings for the (simulated) payment provider. Secrets come from user-secrets/env.</summary>
public sealed class PaymentProviderOptions : ProviderResilienceOptions
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

    /// <summary>Webhooks whose timestamp differs from the server clock by more than this are rejected (replay protection).</summary>
    [Range(30, 3600)]
    public int WebhookTimestampToleranceSeconds { get; init; } = 300;

    public TimeSpan WebhookTimestampTolerance => TimeSpan.FromSeconds(WebhookTimestampToleranceSeconds);
}
