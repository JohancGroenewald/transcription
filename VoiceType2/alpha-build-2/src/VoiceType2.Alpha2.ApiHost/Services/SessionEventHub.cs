using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using VoiceType2.Alpha2.Core;

namespace VoiceType2.Alpha2.ApiHost.Services;

public sealed class SessionEventHub
{
    private readonly ConcurrentDictionary<string, SubscriberSet> _subscribers = new(StringComparer.Ordinal);

    public async Task PublishAsync(string sessionId, SessionEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (!_subscribers.TryGetValue(sessionId, out var subscriberSet))
        {
            return;
        }

        Channel<SessionEventEnvelope>[] channels;
        lock (subscriberSet.SyncRoot)
        {
            channels = [.. subscriberSet.Channels];
        }

        foreach (var channel in channels)
        {
            await channel.Writer.WriteAsync(envelope, cancellationToken);
        }
    }

    public async IAsyncEnumerable<SessionEventEnvelope> SubscribeAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<SessionEventEnvelope>();
        var subscriberSet = _subscribers.GetOrAdd(sessionId, _ => new SubscriberSet());

        lock (subscriberSet.SyncRoot)
        {
            subscriberSet.Channels.Add(channel);
        }

        using var registration = cancellationToken.Register(() => channel.Writer.TryComplete());

        try
        {
            while (await channel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (channel.Reader.TryRead(out var envelope))
                {
                    yield return envelope;
                }
            }
        }
        finally
        {
            lock (subscriberSet.SyncRoot)
            {
                subscriberSet.Channels.Remove(channel);
                if (subscriberSet.Channels.Count == 0)
                {
                    _subscribers.TryRemove(sessionId, out _);
                }
            }
        }
    }

    private sealed class SubscriberSet
    {
        public object SyncRoot { get; } = new();
        public List<Channel<SessionEventEnvelope>> Channels { get; } = [];
    }
}
