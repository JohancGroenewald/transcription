using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using VoiceType2.Core.Contracts;

namespace VoiceType2.ApiHost.Services;

public sealed class SessionService
{
    private readonly ConcurrentDictionary<string, SessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly SessionPolicyConfig _policy;

    public SessionService()
        : this(new SessionPolicyConfig())
    {
    }

    public SessionService(SessionPolicyConfig policy)
    {
        _policy = policy;
    }

    public Task<SessionRecord> CreateAsync(RegisterSessionRequest request, CancellationToken cancellationToken = default)
    {
        CleanupExpiredSessions(DateTimeOffset.UtcNow);

        var profile = request.Profile ?? new OrchestratorProfile();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? $"corr-{Guid.NewGuid():N}"
            : request.CorrelationId;

        if (_sessions.Count(kvp => !IsTerminal(kvp.Value.State)) >= _policy.MaxConcurrentSessions)
        {
            throw new SessionServiceException(
                409,
                "SESSION_LIMIT_EXCEEDED",
                "Maximum concurrent sessions reached.");
        }

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

    public int ActiveSessionCount => _sessions.Count(kvp => !IsTerminal(kvp.Value.State));

    public List<string> CleanupExpiredSessions(DateTimeOffset nowUtc)
    {
        var expiredIds = new List<string>();
        foreach (var session in _sessions)
        {
            if (!IsExpired(session.Value, nowUtc))
            {
                continue;
            }

            if (_sessions.TryRemove(session.Key, out _))
            {
                _locks.TryRemove(session.Key, out _);
                expiredIds.Add(session.Key);
            }
        }

        return expiredIds;
    }

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

    private bool IsExpired(SessionRecord session, DateTimeOffset nowUtc)
    {
        if (session.State == SessionState.Stopped || session.State == SessionState.Completed || session.State == SessionState.Failed)
        {
            if (_policy.DefaultSessionTimeoutMs > 0 &&
                nowUtc - session.LastUpdatedUtc >= TimeSpan.FromMilliseconds(_policy.DefaultSessionTimeoutMs))
            {
                return true;
            }
        }

        if (_policy.SessionIdleTimeoutMs > 0 &&
            nowUtc - session.LastUpdatedUtc >= TimeSpan.FromMilliseconds(_policy.SessionIdleTimeoutMs))
        {
            return true;
        }

        return false;
    }

    private static bool IsTerminal(SessionState state) =>
        state is SessionState.Completed or SessionState.Stopped or SessionState.Failed;

    private static string CreateToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
    }
}

public sealed class SessionServiceException : Exception
{
    public SessionServiceException(int statusCode, string errorCode, string detail)
        : base(detail)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public string ErrorCode { get; }
    public string Detail => Message;
}
