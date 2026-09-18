using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Infrastructure.Providers.Deliveries;

/// <summary>
/// Connection and resilience settings for the (simulated, Uber-like) delivery provider — section <c>Providers:Delivery</c>.
/// Credentials come from user-secrets/env; the values are fake and only the simulator accepts them.
/// </summary>
public sealed class DeliveryProviderOptions : ProviderResilienceOptions
{
    public const string SectionName = "Providers:Delivery";

    [Required]
    [Url]
    public required string BaseUrl { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string ClientId { get; init; }

    [Required(AllowEmptyStrings = false)]
    public required string ClientSecret { get; init; }

    /// <summary>The provider's <c>customer_id</c> path segment for our account.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string CustomerId { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MinLength(16)]
    public required string WebhookSigningKey { get; init; }

    /// <summary>A cached token is renewed this long before its expiry so requests never carry a token about to expire.</summary>
    [Range(5, 3600)]
    public int TokenRefreshSkewSeconds { get; init; } = 60;

    public TimeSpan TokenRefreshSkew => TimeSpan.FromSeconds(TokenRefreshSkewSeconds);
}
