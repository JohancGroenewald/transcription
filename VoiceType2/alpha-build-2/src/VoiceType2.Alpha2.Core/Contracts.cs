namespace VoiceType2.Alpha2.Core;

public enum DictationSessionState
{
    Registered,
    Recording,
    Transcribing,
    Completed,
    Cancelled,
    Failed
}

public sealed class OrchestratorCapabilities
{
    public bool Hotkeys { get; init; }
    public bool Tray { get; init; }
    public bool Clipboard { get; init; } = true;
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
    public OrchestratorCapabilities Capabilities { get; init; } = new();
    public string Version { get; init; } = "v2-alpha2";
}

public sealed class RegisterSessionRequest
{
    public string? CorrelationId { get; init; }
    public string ContractVersion { get; init; } = "v2-alpha2";
    public OrchestratorProfile? Profile { get; init; }
}

public sealed class SessionCreatedResponse
{
    public string SessionId { get; init; } = string.Empty;
    public string OrchestratorToken { get; init; } = string.Empty;
    public string State { get; init; } = DictationSessionState.Registered.ToString();
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class SessionStatusResponse
{
    public string SessionId { get; init; } = string.Empty;
    public string State { get; init; } = DictationSessionState.Registered.ToString();
    public string CorrelationId { get; init; } = string.Empty;
    public string? LastEvent { get; init; }
    public string? Transcript { get; init; }
    public string? Provider { get; init; }
    public AudioCaptureSummary? Audio { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public int Revision { get; init; }
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
    public AudioCaptureSummary? Audio { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AudioInputOptions(
    int PreferredDeviceIndex = -1,
    string PreferredDeviceName = "",
    int MaxDurationSeconds = 300);

public sealed record CaptureDeviceSelection
{
    public int RequestedDeviceIndex { get; init; } = -1;
    public string RequestedDeviceName { get; init; } = string.Empty;
    public int ActiveDeviceIndex { get; init; } = -1;
    public string ActiveDeviceName { get; init; } = string.Empty;
    public string SelectionReason { get; init; } = "not started";
    public bool UsedFallback { get; init; }
    public string LastError { get; init; } = string.Empty;

    public string RequestedSummary => DescribeDevice(RequestedDeviceIndex, RequestedDeviceName);
    public string ActiveSummary => DescribeDevice(ActiveDeviceIndex, ActiveDeviceName);

    public static string DescribeDevice(int deviceIndex, string? deviceName)
    {
        if (deviceIndex < 0)
        {
            return string.IsNullOrWhiteSpace(deviceName)
                ? "system default"
                : deviceName;
        }

        return string.IsNullOrWhiteSpace(deviceName)
            ? $"index {deviceIndex}"
            : $"{deviceName} (index {deviceIndex})";
    }
}

public sealed record AudioCaptureMetrics(
    double DurationSeconds,
    double Rms,
    double Peak,
    double ActiveSampleRatio,
    bool HasAnyNonZeroSample)
{
    public bool IsLikelySilence =>
        DurationSeconds < 0.25 ||
        (!HasAnyNonZeroSample || (Rms < 0.0025 && Peak < 0.012 && ActiveSampleRatio < 0.01));
}

public sealed record AudioCaptureSummary(
    int ByteLength,
    int SampleRate,
    int Channels,
    AudioCaptureMetrics Metrics,
    CaptureDeviceSelection Selection);

public sealed record AudioCaptureResult(
    byte[] WavAudio,
    AudioCaptureSummary Summary);

public sealed record TranscriptionRequest(
    string CorrelationId,
    string Model,
    string? Language,
    string? Prompt,
    bool EnablePrompt);

public sealed record TranscriptionResult(
    string Text,
    string Provider,
    double ProcessingLatencyMs,
    bool IsSuccess,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public interface IAudioInputSource
{
    Task<IAudioCaptureSession> StartAsync(AudioInputOptions options, CancellationToken cancellationToken = default);
}

public interface IAudioCaptureSession : IAsyncDisposable
{
    CaptureDeviceSelection Selection { get; }
    Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default);
    Task CancelAsync(CancellationToken cancellationToken = default);
}

public interface ITranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        AudioCaptureResult capture,
        TranscriptionRequest request,
        CancellationToken cancellationToken = default);
}
