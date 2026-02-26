namespace VoiceType2.Core.Contracts;

public enum SessionState
{
    Uninitialized,
    Registered,
    Running,
    Listening,
    AwaitingDecision,
    Completed,
    Stopped,
    Failed
}

public sealed class OrchestratorCapabilities
{
    public bool Hotkeys { get; init; }
    public bool Tray { get; init; }
    public bool Clipboard { get; init; }
    public bool Notifications { get; init; }
    public bool AudioCapture { get; init; }
    public bool UiShell { get; init; }

    public OrchestratorCapabilities(
        bool hotkeys = false,
        bool tray = false,
        bool clipboard = true,
        bool notifications = false,
        bool audioCapture = false,
        bool uiShell = false)
    {
        Hotkeys = hotkeys;
        Tray = tray;
        Clipboard = clipboard;
        Notifications = notifications;
        AudioCapture = audioCapture;
        UiShell = uiShell;
    }
}

public sealed class OrchestratorProfile
{
    public string OrchestratorId { get; init; } = "cli";
    public string Platform { get; init; } = "windows";
    public OrchestratorCapabilities Capabilities { get; init; } = new OrchestratorCapabilities();
    public string Version { get; init; } = "v1";
}

public sealed class RegisterSessionRequest
{
    public string SessionMode { get; init; } = "dictate";
    public string? CorrelationId { get; init; }
    public string ContractVersion { get; init; } = "v1";
    public OrchestratorProfile? Profile { get; init; }
    public AudioDeviceSelection? AudioDevices { get; init; }
}

public sealed class AudioDeviceSelection
{
    public string? RecordingDeviceId { get; init; }
    public string? PlaybackDeviceId { get; init; }
}

public sealed class HostAudioDevice
{
    public string DeviceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

public sealed class HostDevicesResponse
{
    public IReadOnlyList<HostAudioDevice> RecordingDevices { get; init; } = [];
    public IReadOnlyList<HostAudioDevice> PlaybackDevices { get; init; } = [];
}

public sealed class SessionCreatedResponse
{
    public string SessionId { get; init; } = string.Empty;
    public string OrchestratorToken { get; init; } = string.Empty;
    public string State { get; init; } = SessionState.Registered.ToString();
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class SessionStatusResponse
{
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = SessionState.Uninitialized.ToString();
    public string? CorrelationId { get; init; }
    public AudioDeviceSelection? AudioDevices { get; init; }
    public string? LastEvent { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int Revision { get; init; }
}

public sealed class ResolveRequest
{
    public string Action { get; init; } = string.Empty;
}

public sealed class ErrorEnvelope
{
    public string Type { get; init; } = "about:blank";
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }
    public string Detail { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? CorrelationId { get; init; }
    public string? SessionId { get; init; }
    public string? TraceId { get; init; }
}

public sealed class SessionEventEnvelope
{
    public string EventType { get; init; } = "status";
    public string SessionId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string? State { get; init; }
    public string? Text { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TranscriptionOptions(
    string? Language = null,
    string? Prompt = null,
    bool EnablePrompt = true,
    int? MaxTokens = null);

public sealed record TranscriptionResult(
    string Text,
    string Provider,
    TimeSpan ProcessingLatency,
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? RawPayload = null);

public interface ITranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        Stream audioWav,
        string correlationId,
        TranscriptionOptions? options = null,
        AudioDeviceSelection? audioDevices = null,
        CancellationToken cancellationToken = default);
}

public sealed class SessionRecord
{
    public string SessionId { get; set; } = string.Empty;
    public string OrchestratorToken { get; set; } = string.Empty;
    public OrchestratorProfile Profile { get; set; } = new();
    public string CorrelationId { get; set; } = string.Empty;
    public SessionState State { get; set; } = SessionState.Uninitialized;
    public string? LastEvent { get; set; }
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public AudioDeviceSelection? AudioDevices { get; set; }

    public SessionRecord Clone()
    {
        return new SessionRecord
        {
            SessionId = SessionId,
            OrchestratorToken = OrchestratorToken,
            Profile = Profile,
            CorrelationId = CorrelationId,
            State = State,
            LastEvent = LastEvent,
            Revision = Revision,
            CreatedAt = CreatedAt,
            LastUpdatedUtc = LastUpdatedUtc,
            AudioDevices = AudioDevices
        };
    }
}
