using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.ProviderSimulator.Payments;

/// <summary>Behaviour of the simulated payment provider (docs/INTEGRATIONS.md §3).</summary>
public sealed class PaymentSimulatorOptions
{
    public const string SectionName = "Simulator:Payments";

    /// <summary>Bearer token callers must present. Development value only — this is not a real provider.</summary>
    [Required(AllowEmptyStrings = false)]
    public required string ApiKey { get; init; }

    /// <summary>Key used to sign outgoing webhooks (HMAC-SHA256). Development value only.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(16)]
    public required string WebhookSigningKey { get; init; }

    /// <summary>Where <c>payment.status_changed</c> events are POSTed. Empty disables webhooks.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>Delay between creation and settlement (paid/failed).</summary>
    [Range(0, 600_000)]
    public int SettleDelayMs { get; init; } = 2000;

    /// <summary>Fraction (0–1) of payments that settle as paid; the rest fail with <see cref="DeclineCode"/>.</summary>
    [Range(0, 1)]
    public double ApprovalRate { get; init; } = 1;

    public string DeclineCode { get; init; } = "card_declined";

    /// <summary>Fraction (0–1) of webhooks delivered twice.</summary>
    [Range(0, 1)]
    public double WebhookDuplicateRate { get; init; }

    [Range(0, 600_000)]
    public int WebhookDelayMs { get; init; }

    [Range(1, 1440)]
    public int IdempotencyTtlMinutes { get; init; } = 60;
}
