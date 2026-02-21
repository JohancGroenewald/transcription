using System.Collections.Concurrent;
using System.Threading.Channels;
using VoiceType2.Core.Contracts;

namespace VoiceType2.ApiHost.Services;

internal sealed class SessionEventBus
{
    private readonly ConcurrentDictionary<string, Channel<SessionEventEnvelope>> _channels = new();

    public async Task PublishAsync(string sessionId, SessionEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var channel = GetOrCreateChannel(sessionId);
        await channel.Writer.WriteAsync(envelope, cancellationToken);
    }

    public IAsyncEnumerable<SessionEventEnvelope> SubscribeAsync(string sessionId, CancellationToken cancellationToken)
    {
        var channel = GetOrCreateChannel(sessionId);
        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    public void Complete(string sessionId)
    {
        if (_channels.TryRemove(sessionId, out var channel))
        {
            channel.Writer.TryComplete();
        }
    }

    private static Channel<SessionEventEnvelope> CreateChannel()
    {
        return Channel.CreateUnbounded<SessionEventEnvelope>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    private Channel<SessionEventEnvelope> GetOrCreateChannel(string sessionId)
    {
        return _channels.GetOrAdd(sessionId, _ => CreateChannel());
    }
}
