using System.Threading.Channels;

namespace FulfillmentHub.ProviderSimulator.Common;

/// <summary>Producer/consumer hand-off between the simulated providers and the <see cref="WebhookDispatcher"/>.</summary>
public sealed class WebhookOutbox
{
    private readonly Channel<OutgoingWebhook> _channel = Channel.CreateUnbounded<OutgoingWebhook>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<OutgoingWebhook> Reader => _channel.Reader;

    public void Enqueue(OutgoingWebhook webhook) => _channel.Writer.TryWrite(webhook);
}
