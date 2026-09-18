using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.ProviderSimulator.Deliveries;

/// <summary>Configuration of the simulated delivery provider (section <c>Simulator:Delivery</c>).</summary>
public sealed class DeliverySimulatorOptions
{
    public const string SectionName = "Simulator:Delivery";

    /// <summary>Fake OAuth client credentials accepted by <c>POST /delivery/oauth/token</c> (dev-only values, not secrets).</summary>
    [Required]
    public string ClientId { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>The only <c>customer_id</c> the simulator knows; any other path segment answers <c>customer_not_found</c>.</summary>
    [Required]
    public string CustomerId { get; init; } = "cus_sim_fulfillmenthub";

    /// <summary>Short on purpose so the client's token renewal is exercised (the real provider issues 30-day tokens).</summary>
    [Range(5, 86400)]
    public int TokenLifetimeSeconds { get; init; } = 300;

    [Required]
    [MinLength(16)]
    public string WebhookSigningKey { get; init; } = string.Empty;

    /// <summary>Where <c>event.delivery_status</c> webhooks are posted; empty disables sending.</summary>
    public string? WebhookUrl { get; init; }

    [Range(1, 86400)]
    public int QuoteTtlSeconds { get; init; } = 900;

    /// <summary>Time from creation until a courier is assigned (<c>pending → pickup</c>).</summary>
    [Range(0, 600000)]
    public int CourierAssignMs { get; init; } = 1000;

    /// <summary>Interval between the remaining status transitions.</summary>
    [Range(0, 600000)]
    public int StepMs { get; init; } = 3000;

    [Range(0, 1000000)]
    public int BaseFeeCents { get; init; } = 1200;

    /// <summary>Fraction (0–1) of quote/create calls answered with <c>503 couriers_busy</c>.</summary>
    [Range(0, 1)]
    public double CouriersBusyRate { get; init; }

    [Range(0, 1)]
    public double WebhookDuplicateRate { get; init; }

    [Range(0, 600000)]
    public int WebhookDelayMs { get; init; }

    /// <summary>Sends consecutive status webhooks in shuffled order (Phase 7 scenario).</summary>
    public bool WebhookOutOfOrder { get; init; }

    [Range(1, 1440)]
    public int IdempotencyTtlMinutes { get; init; } = 60;
}
