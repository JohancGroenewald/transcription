using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using VoiceType2.Core.Contracts;

namespace VoiceType2.ApiHost.Services;

internal sealed class SessionService
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public Task<SessionRecord> CreateAsync(RegisterSessionRequest request, CancellationToken cancellationToken = default)
    {
        var profile = request.Profile ?? new OrchestratorProfile();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"corr-{Guid.NewGuid():N}"
            : request.CorrelationId;

        var session = new SessionRecord
        {
            SessionId = Guid.NewGuid().ToString("N"),
            OrchestratorToken = CreateToken(),
            Profile = profile,
            CorrelationId = correlationId,
            State = SessionState.Registered,
            LastEvent = "created",
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            Revision = 1
        };

        _sessions[session.SessionId] = session;
        return Task.FromResult(session.Clone());
    }

    public bool TryGet(string sessionId, [NotNullWhen(true)] out SessionRecord? session)
    {
        if (_sessions.TryGetValue(sessionId, out var foundSession))
        {
            session = foundSession.Clone();
            return true;
        }

        session = null;
        return false;
    }

    public Task<bool> TryTransitionAsync(
        string sessionId,
        Func<SessionState, bool> canTransition,
        SessionState nextState,
        string lastEvent,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult(false);
        }

        var lockObject = _locks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        return TryTransitionInternalAsync(session, lockObject, canTransition, nextState, lastEvent, cancellationToken);
    }

    public Task<bool> TryResetAsync(string sessionId, SessionState targetState, string lastEvent, CancellationToken cancellationToken)
    {
        return TryTransitionAsync(sessionId, _ => true, targetState, lastEvent, cancellationToken);
    }

    public int ActiveSessionCount => _sessions.Count;

    private static async Task<bool> TryTransitionInternalAsync(
        SessionRecord session,
        SemaphoreSlim lockObject,
        Func<SessionState, bool> canTransition,
        SessionState nextState,
        string lastEvent,
        CancellationToken cancellationToken)
    {
        await lockObject.WaitAsync(cancellationToken);

        try
        {
            if (!canTransition(session.State))
            {
                return false;
            }

            session.State = nextState;
            session.Revision++;
            session.LastEvent = lastEvent;
            session.LastUpdatedUtc = DateTimeOffset.UtcNow;
            return true;
        }
        finally
        {
            lockObject.Release();
        }
    }

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }
}
