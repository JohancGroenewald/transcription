using System.Collections.Concurrent;
using VoiceType2.Alpha2.Core;

namespace VoiceType2.Alpha2.ApiHost.Services;

public sealed class SessionCoordinatorException(int statusCode, string errorCode, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public sealed class SessionCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, StoredSession> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IAudioCaptureSession> _activeCaptures = new(StringComparer.Ordinal);
    private readonly RuntimeConfig _config;
    private readonly IAudioInputSource _audioInputSource;
    private readonly ITranscriptionProvider _transcriptionProvider;
    private readonly SessionEventHub _eventHub;
    private readonly ILogger<SessionCoordinator> _logger;

    public SessionCoordinator(
        RuntimeConfig config,
        IAudioInputSource audioInputSource,
        ITranscriptionProvider transcriptionProvider,
        SessionEventHub eventHub,
        ILogger<SessionCoordinator> logger)
    {
        _config = config;
        _audioInputSource = audioInputSource;
        _transcriptionProvider = transcriptionProvider;
        _eventHub = eventHub;
        _logger = logger;
    }

    public SessionCreatedResponse Register(RegisterSessionRequest? request)
    {
        request ??= new RegisterSessionRequest();

        var storedSession = new StoredSession
        {
            SessionId = Guid.NewGuid().ToString("N"),
            OrchestratorToken = Guid.NewGuid().ToString("N"),
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? $"corr-{Guid.NewGuid():N}"
                : request.CorrelationId,
            Profile = request.Profile ?? new OrchestratorProfile(),
            State = DictationSessionState.Registered,
            LastEvent = "registered"
        };

        lock (_sync)
        {
            _sessions[storedSession.SessionId] = storedSession;
        }

        return storedSession.ToCreatedResponse();
    }

    public SessionStatusResponse GetStatus(string sessionId, string? token)
    {
        var session = GetAuthorizedSession(sessionId, token);
        return session.ToStatusResponse();
    }

    public async Task<SessionStatusResponse> StartAsync(string sessionId, string? token, CancellationToken cancellationToken)
    {
        var session = GetAuthorizedSession(sessionId, token);
        if (session.State == DictationSessionState.Recording)
        {
            return session.ToStatusResponse();
        }

        if (session.State is not DictationSessionState.Registered)
        {
            throw new SessionCoordinatorException(409, "INVALID_TRANSITION", "Session can only be started from Registered state.");
        }

        UpdateSession(sessionId, state =>
        {
            state.State = DictationSessionState.Recording;
            state.LastEvent = "recording-started";
            state.ErrorCode = null;
            state.ErrorMessage = null;
            state.Revision++;
        });

        try
        {
            var capture = await _audioInputSource.StartAsync(
                new AudioInputOptions(
                    _config.AudioCapture.PreferredDeviceIndex,
                    _config.AudioCapture.PreferredDeviceName,
                    _config.AudioCapture.MaxDurationSeconds),
                cancellationToken);

            if (!_activeCaptures.TryAdd(sessionId, capture))
            {
                await capture.CancelAsync(cancellationToken);
                await capture.DisposeAsync();
                throw new SessionCoordinatorException(409, "SESSION_ALREADY_ACTIVE", "Session recording is already active.");
            }

            session = GetSnapshot(sessionId);
            await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "status",
                SessionId = sessionId,
                CorrelationId = session.CorrelationId,
                State = DictationSessionState.Recording.ToString(),
                Text = "recording-started"
            }, cancellationToken);

            return session.ToStatusResponse();
        }
        catch (SessionCoordinatorException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start audio capture for session {SessionId}.", sessionId);
            session = UpdateSession(sessionId, state =>
            {
                state.State = DictationSessionState.Failed;
                state.LastEvent = "recording-start-failed";
                state.ErrorCode = "AUDIO_CAPTURE_START_FAILED";
                state.ErrorMessage = ex.Message;
                state.Revision++;
            });

            await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "error",
                SessionId = sessionId,
                CorrelationId = session.CorrelationId,
                State = DictationSessionState.Failed.ToString(),
                ErrorCode = session.ErrorCode,
                ErrorMessage = session.ErrorMessage,
                Text = "recording-start-failed"
            }, cancellationToken);

            return session.ToStatusResponse();
        }
    }

    public async Task<SessionStatusResponse> StopAsync(string sessionId, string? token, CancellationToken cancellationToken)
    {
        var session = GetAuthorizedSession(sessionId, token);
        if (session.State == DictationSessionState.Completed ||
            session.State == DictationSessionState.Cancelled ||
            session.State == DictationSessionState.Failed)
        {
            return session.ToStatusResponse();
        }

        if (session.State is not DictationSessionState.Recording)
        {
            throw new SessionCoordinatorException(409, "INVALID_TRANSITION", "Session can only be stopped from Recording state.");
        }

        if (!_activeCaptures.TryRemove(sessionId, out var capture))
        {
            throw new SessionCoordinatorException(409, "CAPTURE_NOT_ACTIVE", "No active capture exists for this session.");
        }

        session = UpdateSession(sessionId, state =>
        {
            state.State = DictationSessionState.Transcribing;
            state.LastEvent = "transcribing";
            state.Revision++;
        });

        await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = sessionId,
            CorrelationId = session.CorrelationId,
            State = DictationSessionState.Transcribing.ToString(),
            Text = "transcribing"
        }, cancellationToken);

        try
        {
            await using var ownedCapture = capture;
            var captureResult = await capture.StopAsync(cancellationToken);

            session = UpdateSession(sessionId, state =>
            {
                state.Audio = captureResult.Summary;
                state.LastEvent = "audio-captured";
                state.Revision++;
            });

            await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "audio",
                SessionId = sessionId,
                CorrelationId = session.CorrelationId,
                State = DictationSessionState.Transcribing.ToString(),
                Audio = captureResult.Summary,
                Text = "audio-captured"
            }, cancellationToken);

            var result = await _transcriptionProvider.TranscribeAsync(
                captureResult,
                new TranscriptionRequest(
                    session.CorrelationId,
                    _config.Transcription.Model,
                    _config.Transcription.Language,
                    _config.Transcription.Prompt,
                    _config.Transcription.EnablePrompt),
                cancellationToken);

            if (!result.IsSuccess)
            {
                session = UpdateSession(sessionId, state =>
                {
                    state.State = DictationSessionState.Failed;
                    state.Provider = result.Provider;
                    state.LastEvent = "transcription-failed";
                    state.ErrorCode = result.ErrorCode;
                    state.ErrorMessage = result.ErrorMessage;
                    state.Revision++;
                });

                await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
                {
                    EventType = "error",
                    SessionId = sessionId,
                    CorrelationId = session.CorrelationId,
                    State = DictationSessionState.Failed.ToString(),
                    ErrorCode = result.ErrorCode,
                    ErrorMessage = result.ErrorMessage,
                    Text = "transcription-failed"
                }, cancellationToken);

                return session.ToStatusResponse();
            }

            session = UpdateSession(sessionId, state =>
            {
                state.State = DictationSessionState.Completed;
                state.Provider = result.Provider;
                state.Transcript = result.Text;
                state.LastEvent = "completed";
                state.ErrorCode = null;
                state.ErrorMessage = null;
                state.Revision++;
            });

            await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "transcript",
                SessionId = sessionId,
                CorrelationId = session.CorrelationId,
                Text = result.Text
            }, cancellationToken);

            await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "status",
                SessionId = sessionId,
                CorrelationId = session.CorrelationId,
                State = DictationSessionState.Completed.ToString(),
                Text = "completed",
                Audio = session.Audio
            }, cancellationToken);

            return session.ToStatusResponse();
        }
        catch (SessionCoordinatorException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stopping session {SessionId} failed.", sessionId);
            session = UpdateSession(sessionId, state =>
            {
                state.State = DictationSessionState.Failed;
                state.LastEvent = "stop-failed";
                state.ErrorCode = "STOP_FAILED";
                state.ErrorMessage = ex.Message;
                state.Revision++;
            });

            await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "error",
                SessionId = sessionId,
                CorrelationId = session.CorrelationId,
                State = DictationSessionState.Failed.ToString(),
                ErrorCode = session.ErrorCode,
                ErrorMessage = session.ErrorMessage,
                Text = "stop-failed"
            }, cancellationToken);

            return session.ToStatusResponse();
        }
    }

    public async Task<SessionStatusResponse> CancelAsync(string sessionId, string? token, CancellationToken cancellationToken)
    {
        var session = GetAuthorizedSession(sessionId, token);
        if (session.State == DictationSessionState.Cancelled)
        {
            return session.ToStatusResponse();
        }

        if (_activeCaptures.TryRemove(sessionId, out var capture))
        {
            await using var ownedCapture = capture;
            await capture.CancelAsync(cancellationToken);
        }

        session = UpdateSession(sessionId, state =>
        {
            state.State = DictationSessionState.Cancelled;
            state.LastEvent = "cancelled";
            state.Revision++;
        });

        await _eventHub.PublishAsync(sessionId, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = sessionId,
            CorrelationId = session.CorrelationId,
            State = DictationSessionState.Cancelled.ToString(),
            Text = "cancelled"
        }, cancellationToken);

        return session.ToStatusResponse();
    }

    public StoredSession GetAuthorizedSession(string sessionId, string? token)
    {
        var session = GetSnapshot(sessionId);

        if (_config.IsTokenAuthAllowed)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                if (_config.IsTokenAuthRequired)
                {
                    throw new SessionCoordinatorException(401, "INVALID_TOKEN", "Missing orchestrator token.");
                }
            }
            else if (!string.Equals(token, session.OrchestratorToken, StringComparison.Ordinal))
            {
                throw new SessionCoordinatorException(401, "INVALID_TOKEN", "Invalid orchestrator token.");
            }
        }

        return session;
    }

    private StoredSession GetSnapshot(string sessionId)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new SessionCoordinatorException(404, "SESSION_NOT_FOUND", "Session not found.");
            }

            return session.Clone();
        }
    }

    private StoredSession UpdateSession(string sessionId, Action<StoredSession> update)
    {
        lock (_sync)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new SessionCoordinatorException(404, "SESSION_NOT_FOUND", "Session not found.");
            }

            update(session);
            return session.Clone();
        }
    }

    public sealed class StoredSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string OrchestratorToken { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public OrchestratorProfile Profile { get; set; } = new();
        public DictationSessionState State { get; set; } = DictationSessionState.Registered;
        public string? LastEvent { get; set; }
        public string? Transcript { get; set; }
        public string? Provider { get; set; }
        public AudioCaptureSummary? Audio { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int Revision { get; set; } = 1;

        public StoredSession Clone()
        {
            return new StoredSession
            {
                SessionId = SessionId,
                OrchestratorToken = OrchestratorToken,
                CorrelationId = CorrelationId,
                Profile = Profile,
                State = State,
                LastEvent = LastEvent,
                Transcript = Transcript,
                Provider = Provider,
                Audio = Audio,
                ErrorCode = ErrorCode,
                ErrorMessage = ErrorMessage,
                Revision = Revision
            };
        }

        public SessionCreatedResponse ToCreatedResponse()
        {
            return new SessionCreatedResponse
            {
                SessionId = SessionId,
                OrchestratorToken = OrchestratorToken,
                State = State.ToString(),
                CorrelationId = CorrelationId
            };
        }

        public SessionStatusResponse ToStatusResponse()
        {
            return new SessionStatusResponse
            {
                SessionId = SessionId,
                State = State.ToString(),
                CorrelationId = CorrelationId,
                LastEvent = LastEvent,
                Transcript = Transcript,
                Provider = Provider,
                Audio = Audio,
                ErrorCode = ErrorCode,
                ErrorMessage = ErrorMessage,
                Revision = Revision
            };
        }
    }
}
