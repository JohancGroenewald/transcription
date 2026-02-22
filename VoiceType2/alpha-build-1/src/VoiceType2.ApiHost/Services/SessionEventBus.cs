using System.Collections.Concurrent;
using System.Threading.Channels;
using VoiceType2.Core.Contracts;

namespace VoiceType2.ApiHost.Services;

internal sealed class SessionEventBus
{
    private readonly ConcurrentDictionary<string, Channel<SessionEventEnvelope>> _channels = new();

    public async Task PublishAsync(string sessionId, SessionEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(sessionId, out var channel))
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!channel.Writer.TryWrite(envelope))
        {
            await channel.Writer.WriteAsync(envelope, cancellationToken);
        }
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
        return Channel.CreateBounded<SessionEventEnvelope>(new BoundedChannelOptions(128)
        {
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });
    }

    private Channel<SessionEventEnvelope> GetOrCreateChannel(string sessionId)
    {
        return _channels.GetOrAdd(sessionId, _ => CreateChannel());
    }
}
