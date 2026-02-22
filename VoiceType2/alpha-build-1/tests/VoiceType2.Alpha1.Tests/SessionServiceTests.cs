using VoiceType2.ApiHost;
using VoiceType2.ApiHost.Services;
using VoiceType2.Core.Contracts;

namespace VoiceType2.Alpha1.Tests;

public class SessionServiceTests
{
    [Fact]
    public async Task CreateAsync_registers_session_with_expected_defaults()
    {
        var service = new SessionService(new SessionPolicyConfig
        {
            MaxConcurrentSessions = 2,
            DefaultSessionTimeoutMs = 300000,
            SessionIdleTimeoutMs = 120000
        });

        var created = await service.CreateAsync(new RegisterSessionRequest
        {
            SessionMode = "dictate",
            CorrelationId = "corr-test-1"
        });

        Assert.Equal("corr-test-1", created.CorrelationId);
        Assert.Equal(SessionState.Registered, created.State);
        Assert.False(string.IsNullOrWhiteSpace(created.SessionId));
        Assert.False(string.IsNullOrWhiteSpace(created.OrchestratorToken));
    }

    [Fact]
    public async Task CreateAsync_enforces_max_concurrent_session_limit()
    {
        var service = new SessionService(new SessionPolicyConfig { MaxConcurrentSessions = 1 });
        _ = await service.CreateAsync(new RegisterSessionRequest { CorrelationId = "corr-1" });

        var exception = await Assert.ThrowsAsync<SessionServiceException>(() =>
            service.CreateAsync(new RegisterSessionRequest { CorrelationId = "corr-2" }));

        Assert.Equal("SESSION_LIMIT_EXCEEDED", exception.ErrorCode);
        Assert.Equal(409, exception.StatusCode);
    }

    [Fact]
    public async Task TryTransitionAsync_blocks_invalid_state_flow()
    {
        var service = new SessionService(new SessionPolicyConfig { MaxConcurrentSessions = 2 });
        var session = await service.CreateAsync(new RegisterSessionRequest());

        Assert.True(await service.TryTransitionAsync(
            session.SessionId,
            state => state is SessionState.Registered,
            SessionState.Listening,
            "start",
            CancellationToken.None));

        Assert.False(await service.TryTransitionAsync(
            session.SessionId,
            state => state is SessionState.Registered,
            SessionState.Stopped,
            "bad",
            CancellationToken.None));
    }

    [Fact]
    public async Task TryTransitionAsync_updates_revision_and_last_event()
    {
        var service = new SessionService(new SessionPolicyConfig { MaxConcurrentSessions = 2 });
        var session = await service.CreateAsync(new RegisterSessionRequest());
        var expectedRevision = session.Revision + 1;

        Assert.True(await service.TryTransitionAsync(
            session.SessionId,
            state => state is SessionState.Registered,
            SessionState.Listening,
            "start",
            CancellationToken.None));

        Assert.True(service.TryGet(session.SessionId, out var updated));
        Assert.Equal(expectedRevision, updated!.Revision);
        Assert.Equal("start", updated.LastEvent);
    }

    [Fact]
    public async Task CleanupExpiredSessions_removes_timed_out_records()
    {
        var service = new SessionService(new SessionPolicyConfig
        {
            MaxConcurrentSessions = 2,
            SessionIdleTimeoutMs = 1,
            DefaultSessionTimeoutMs = 1
        });

        var session = await service.CreateAsync(new RegisterSessionRequest());
        await Task.Delay(5);

        var expired = service.CleanupExpiredSessions(DateTimeOffset.UtcNow);
        Assert.Contains(session.SessionId, expired);
        Assert.False(service.TryGet(session.SessionId, out _));
        Assert.Equal(0, service.ActiveSessionCount);
    }
}
