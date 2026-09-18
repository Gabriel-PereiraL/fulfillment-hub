using System.ComponentModel.DataAnnotations;

namespace FulfillmentHub.Api.Identity;

/// <summary>Per-client-IP fixed-window budgets (section <c>RateLimiting</c>).</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Login attempts per minute per IP (credential stuffing, OWASP A07).</summary>
    [Range(1, 1000)]
    public int LoginPerMinute { get; init; } = 5;

    /// <summary>
    /// Webhooks per minute per IP. Providers send from a few IPs and a delivery alone produces 4–5 events, so the
    /// budget is generous; it only has to blunt a flood, providers retry with backoff on 429.
    /// </summary>
    [Range(1, 100_000)]
    public int WebhooksPerMinute { get; init; } = 1200;
}
