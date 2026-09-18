namespace FulfillmentHub.Application.Webhooks;

/// <summary>What processing a stored webhook event concluded (drives the inbox status).</summary>
public abstract record WebhookOutcome
{
    public sealed record Processed : WebhookOutcome;

    public sealed record Ignored(string Reason) : WebhookOutcome;

    public sealed record Failed(string Error) : WebhookOutcome;

    public static readonly WebhookOutcome Done = new Processed();
}
