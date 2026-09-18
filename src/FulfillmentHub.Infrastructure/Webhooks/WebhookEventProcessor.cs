using FulfillmentHub.Application.Webhooks;
using FulfillmentHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FulfillmentHub.Infrastructure.Webhooks;

/// <summary>
/// Processes a stored webhook event exactly once: loads it, hands it to the provider's <see cref="IWebhookProcessor"/>
/// and records the outcome in the inbox. An event that is no longer <c>Received</c> is acknowledged without work, which
/// is what makes queue redeliveries and the in-process fallback safe to combine.
/// </summary>
public sealed partial class WebhookEventProcessor(IServiceScopeFactory scopeFactory, WebhookInbox inbox, ILogger<WebhookEventProcessor> logger)
{
    public async Task<WebhookOutcome> ProcessAsync(Guid webhookEventId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FulfillmentHubDbContext>();
        var webhookEvent = await db.WebhookEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == webhookEventId, cancellationToken);

        if (webhookEvent is null)
        {
            return new WebhookOutcome.Ignored($"webhook event {webhookEventId} does not exist");
        }

        if (webhookEvent.Status != WebhookEventStatus.Received)
        {
            LogAlreadyHandled(webhookEvent.Provider, webhookEvent.ProviderEventId, webhookEvent.Status);
            return WebhookOutcome.Done;
        }

        var processor = scope.ServiceProvider.GetKeyedService<IWebhookProcessor>(webhookEvent.Provider);
        if (processor is null)
        {
            await inbox.MarkIgnoredAsync(webhookEvent.Id, $"no processor for provider '{webhookEvent.Provider}'", cancellationToken);
            return new WebhookOutcome.Ignored("no processor for provider");
        }

        WebhookOutcome outcome;
        try
        {
            outcome = await processor.ProcessAsync(webhookEvent, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogProcessingFailed(exception, webhookEvent.Provider, webhookEvent.ProviderEventId);
            outcome = new WebhookOutcome.Failed(exception.GetType().Name + ": " + exception.Message);
        }

        switch (outcome)
        {
            case WebhookOutcome.Processed:
                await inbox.MarkProcessedAsync(webhookEvent.Id, CancellationToken.None);
                break;
            case WebhookOutcome.Ignored ignored:
                await inbox.MarkIgnoredAsync(webhookEvent.Id, ignored.Reason, CancellationToken.None);
                break;
            case WebhookOutcome.Failed failed:
                await inbox.MarkFailedAsync(webhookEvent.Id, failed.Error, CancellationToken.None);
                break;
        }

        return outcome;
    }

    [LoggerMessage(EventId = 5210, Level = LogLevel.Information, Message = "Webhook {Provider}/{ProviderEventId} already {Status}; acknowledging")]
    private partial void LogAlreadyHandled(string provider, string providerEventId, WebhookEventStatus status);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "Processing webhook {Provider}/{ProviderEventId} failed")]
    private partial void LogProcessingFailed(Exception exception, string provider, string providerEventId);
}
