using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using VoiceType2.ApiHost;
using VoiceType2.ApiHost.Services;
using VoiceType2.Core.Contracts;
using VoiceType2.Infrastructure.Transcription;

var options = ApiHostOptions.Parse(args);

if (options.ShowHelp)
{
    PrintUsage();
    return;
}

if (!string.Equals(options.Mode, "service", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"Unsupported mode '{options.Mode}'. Supported mode: service.");
    Environment.ExitCode = 1;
    return;
}

RuntimeConfig config;
try
{
    config = RuntimeConfig.Load(options.ConfigPath);

    if (!string.IsNullOrWhiteSpace(options.Urls))
    {
        config.HostBinding.Urls = options.Urls;
    }

    config.Validate();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load runtime config: {ex.Message}");
    Environment.ExitCode = 1;
    return;
}

var builder = WebApplication.CreateBuilder(Array.Empty<string>());
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss ";
});

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(config.SessionPolicy);
builder.Services.AddSingleton(config.RuntimeSecurity);
builder.Services.AddSingleton(config.TranscriptionDefaults);
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<SessionEventBus>();
builder.Services.AddSingleton<ITranscriptionProvider, MockTranscriptionProvider>();

builder.WebHost.UseUrls(config.HostBinding.Urls);

var app = builder.Build();
var activeWorkers = new ConcurrentDictionary<string, SessionWorkItem>();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/health/ready", (SessionService sessions) => Results.Ok(new
{
    status = "ready",
    sessionCount = sessions.ActiveSessionCount,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapPost("/v1/sessions", async (
    RegisterSessionRequest request,
    SessionService sessions,
    SessionEventBus eventBus,
    HttpContext context) =>
{
    RunHousekeeping(sessions, eventBus, activeWorkers);
    request ??= new RegisterSessionRequest();
    var requestCorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
        ? $"corr-{Guid.NewGuid():N}"
        : request.CorrelationId;

    try
    {
        var audioValidationResult = ValidateAudioDeviceSelection(request.AudioDevices, requestCorrelationId);
        if (audioValidationResult is not null)
        {
            return audioValidationResult;
        }

        var session = await sessions.CreateAsync(request, context.RequestAborted);
        await eventBus.PublishAsync(session.SessionId, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = session.SessionId,
            CorrelationId = session.CorrelationId,
            State = session.State.ToString(),
            Text = "registered"
        }, context.RequestAborted);

        return Results.Ok(new SessionCreatedResponse
        {
            SessionId = session.SessionId,
            OrchestratorToken = session.OrchestratorToken,
            State = session.State.ToString(),
            CorrelationId = session.CorrelationId
        });
    }
    catch (SessionServiceException ex)
    {
        return Problem(ex.StatusCode, ex.ErrorCode, ex.Detail, requestCorrelationId);
    }
});

app.MapGet("/v1/sessions/{sessionId}", (string sessionId, HttpContext context, RuntimeConfig config, SessionService sessions) =>
{
    RunHousekeeping(sessions, null, activeWorkers);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, config, session))
    {
        return Problem(401, "INVALID_TOKEN", "Missing or invalid orchestrator token.");
    }

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = session.SessionId,
        State = session.State.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = session.LastEvent,
        Revision = session.Revision,
        AudioDevices = session.AudioDevices
    });
});

app.MapGet("/v1/devices", () =>
{
    return Results.Ok(new HostDevicesResponse
    {
        RecordingDevices = GetHostRecordingDevices(),
        PlaybackDevices = GetHostPlaybackDevices()
    });
});

app.MapPost("/v1/sessions/{sessionId}/devices", async (
    string sessionId,
    AudioDeviceSelection? request,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    RuntimeConfig config,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus, activeWorkers);

    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, config, session))
    {
        return Problem(401, "INVALID_TOKEN", "Missing or invalid orchestrator token.");
    }

    var validationResult = ValidateAudioDeviceSelection(request, session.CorrelationId);
    if (validationResult is not null)
    {
        return validationResult;
    }

    var updated = await sessions.TryUpdateAudioDevicesAsync(
        sessionId,
        request,
        "audio-devices-updated",
        ct);
    if (!updated)
    {
        return Problem(409, "INVALID_TRANSITION", "Audio device update is only valid for non-terminal sessions.");
    }

    session = sessions.TryGet(sessionId, out var refreshed) ? refreshed : session;
    await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
    {
        EventType = "status",
        SessionId = sessionId,
        CorrelationId = session.CorrelationId,
        State = session.State.ToString(),
        Text = "audio-devices-updated"
    }, ct);

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = session.SessionId,
        State = session.State.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = "audio-devices-updated",
        Revision = session.Revision,
        AudioDevices = session.AudioDevices
    });
});

app.MapPost("/v1/sessions/{sessionId}/start", async (
    string sessionId,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    RuntimeConfig config,
    ITranscriptionProvider transcriptionProvider,
    TranscriptionDefaultsConfig transcriptionDefaults,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus, activeWorkers);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, config, session))
    {
        return Problem(401, "INVALID_TOKEN", "Missing or invalid orchestrator token.");
    }

    if (session.State is SessionState.Listening or SessionState.Running)
    {
        return Results.Ok(new SessionStatusResponse
        {
            SessionId = session.SessionId,
            State = session.State.ToString(),
            CorrelationId = session.CorrelationId,
            LastEvent = "already-listening",
            AudioDevices = session.AudioDevices
        });
    }

    var transitioned = await sessions.TryTransitionAsync(
        sessionId,
        state => state is SessionState.Registered or SessionState.Completed,
        SessionState.Listening,
        "start",
        ct);

    if (!transitioned)
    {
        return Problem(409, "INVALID_TRANSITION", "Start is not valid for current state.");
    }

    session = sessions.TryGet(sessionId, out var refreshed) ? refreshed : session;
    await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
    {
        EventType = "status",
        SessionId = sessionId,
        CorrelationId = session.CorrelationId,
        State = SessionState.Listening.ToString(),
        Text = "started"
    }, ct);

    StartSessionWorkAsync(sessionId, session.CorrelationId, session.AudioDevices, sessions, eventBus, transcriptionProvider, transcriptionDefaults, activeWorkers);

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = sessionId,
        State = SessionState.Listening.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = "started",
        AudioDevices = session.AudioDevices
    });
});

app.MapPost("/v1/sessions/{sessionId}/stop", async (
    string sessionId,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    RuntimeConfig config,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus, activeWorkers);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, config, session))
    {
        return Problem(401, "INVALID_TOKEN", "Missing or invalid orchestrator token.");
    }

    if (session.State == SessionState.Stopped)
    {
        return Results.Ok(new SessionStatusResponse
        {
            SessionId = session.SessionId,
            State = SessionState.Stopped.ToString(),
            CorrelationId = session.CorrelationId,
            LastEvent = session.LastEvent,
            AudioDevices = session.AudioDevices
        });
    }

    var transitioned = await sessions.TryTransitionAsync(
        sessionId,
        state => state is SessionState.Listening or SessionState.AwaitingDecision or SessionState.Running,
        SessionState.Stopped,
        "stop",
        ct);

    if (!transitioned)
    {
        return Problem(409, "INVALID_TRANSITION", "Stop is not valid for current state.");
    }

    StopSessionWork(sessionId, activeWorkers);

    session = sessions.TryGet(sessionId, out var refreshed) ? refreshed : session;
    await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
    {
        EventType = "status",
        SessionId = sessionId,
        CorrelationId = session.CorrelationId,
        State = SessionState.Stopped.ToString(),
        Text = "stopped"
    }, ct);
    eventBus.Complete(sessionId);

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = sessionId,
        State = SessionState.Stopped.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = "stopped",
        AudioDevices = session.AudioDevices
    });
});

app.MapPost("/v1/sessions/{sessionId}/resolve", async (
    string sessionId,
    ResolveRequest request,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    RuntimeConfig config,
    ITranscriptionProvider transcriptionProvider,
    TranscriptionDefaultsConfig transcriptionDefaults,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus, activeWorkers);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, config, session))
    {
        return Problem(401, "INVALID_TOKEN", "Missing or invalid orchestrator token.");
    }

    request ??= new ResolveRequest();
    var action = request.Action?.Trim().ToLowerInvariant();

    if (string.IsNullOrWhiteSpace(action))
    {
        return Problem(400, "INVALID_RESOLVE", "Resolve action is required.");
    }

    var finalState = action switch
    {
        "submit" => SessionState.Completed,
        "cancel" => SessionState.Completed,
        "retry" => SessionState.Listening,
        _ => SessionState.Uninitialized
    };

    if (finalState == SessionState.Uninitialized)
    {
        return Problem(400, "INVALID_RESOLVE", $"Unsupported resolve action '{action}'.", session.SessionId);
    }

    var transitioned = await sessions.TryTransitionAsync(
        sessionId,
        state => state == SessionState.AwaitingDecision,
        finalState,
        $"resolve:{action}",
        ct);

    if (!transitioned)
    {
        return Problem(409, "INVALID_TRANSITION", "Resolve is only valid when session is awaiting decision.");
    }

    session = sessions.TryGet(sessionId, out var refreshed) ? refreshed : session;
    await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
    {
        EventType = "command",
        SessionId = sessionId,
        CorrelationId = session.CorrelationId,
        State = session.State.ToString(),
        Text = $"resolved:{action}"
    }, ct);

    if (action == "retry")
    {
        StartSessionWorkAsync(sessionId, session.CorrelationId, session.AudioDevices, sessions, eventBus, transcriptionProvider, transcriptionDefaults, activeWorkers);
    }
    else
    {
        eventBus.Complete(sessionId);
    }

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = sessionId,
        State = session.State.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = $"resolve:{action}",
        AudioDevices = session.AudioDevices
    });
});

app.MapGet("/v1/sessions/{sessionId}/events", async (
    string sessionId,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    RuntimeConfig config,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus, activeWorkers);
    if (!sessions.TryGet(sessionId, out var session))
    {
        await WriteProblemResponseAsync(context, 404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
        return;
    }

    if (!IsAuthorized(context, config, session))
    {
        await WriteProblemResponseAsync(context, 401, "INVALID_TOKEN", "Missing or invalid orchestrator token.", sessionId);
        return;
    }

    context.Response.Headers["Content-Type"] = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    await WriteSseEventAsync(context, new SessionEventEnvelope
    {
        EventType = "status",
        SessionId = sessionId,
        CorrelationId = session.CorrelationId,
        State = session.State.ToString(),
        Text = "stream-opened",
        Timestamp = DateTimeOffset.UtcNow
    }, ct);

    await foreach (var evt in eventBus.SubscribeAsync(sessionId, ct))
    {
        await WriteSseEventAsync(context, evt, ct);
    }
});

await app.RunAsync();

static IResult? ValidateAudioDeviceSelection(AudioDeviceSelection? audioDevices, string requestCorrelationId)
{
    if (audioDevices is null)
    {
        return null;
    }

    var recordingCandidates = GetHostRecordingDevices();
    var playbackCandidates = GetHostPlaybackDevices();

    if (!string.IsNullOrWhiteSpace(audioDevices.RecordingDeviceId)
        && !ContainsAudioDevice(recordingCandidates, audioDevices.RecordingDeviceId))
    {
        return Problem(
            400,
            "INVALID_RECORDING_DEVICE",
            $"Unknown recording device id '{audioDevices.RecordingDeviceId}'.",
            correlationId: requestCorrelationId);
    }

    if (!string.IsNullOrWhiteSpace(audioDevices.PlaybackDeviceId)
        && !ContainsAudioDevice(playbackCandidates, audioDevices.PlaybackDeviceId))
    {
        return Problem(
            400,
            "INVALID_PLAYBACK_DEVICE",
            $"Unknown playback device id '{audioDevices.PlaybackDeviceId}'.",
            correlationId: requestCorrelationId);
    }

    return null;
}

static bool ContainsAudioDevice(IReadOnlyList<HostAudioDevice> devices, string? deviceId)
{
    if (string.IsNullOrWhiteSpace(deviceId))
    {
        return false;
    }

    if (devices.Count == 0)
    {
        return true;
    }

    foreach (var device in devices)
    {
        if (string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static IReadOnlyList<HostAudioDevice> GetHostRecordingDevices()
{
    if (OperatingSystem.IsWindows())
    {
        return GetHostWaveDevices("NAudio.Wave.WaveIn", "rec");
    }

    if (OperatingSystem.IsLinux())
    {
        return ParseHostAlsaDevices("arecord -l", "rec");
    }

    if (OperatingSystem.IsMacOS())
    {
        return ParseHostMacDevices(true);
    }

    return [];
}

static IReadOnlyList<HostAudioDevice> GetHostPlaybackDevices()
{
    if (OperatingSystem.IsWindows())
    {
        return GetHostWaveDevices("NAudio.Wave.WaveOut", "play");
    }

    if (OperatingSystem.IsLinux())
    {
        return ParseHostAlsaDevices("aplay -l", "play");
    }

    if (OperatingSystem.IsMacOS())
    {
        return ParseHostMacDevices(false);
    }

    return [];
}

static IReadOnlyList<HostAudioDevice> GetHostWaveDevices(string typeName, string prefix)
{
    try
    {
        var type = Type.GetType($"{typeName}, NAudio");
        if (type is null)
        {
            return [];
        }

        var deviceCountProperty = type.GetProperty(
            "DeviceCount",
            BindingFlags.Public | BindingFlags.Static);
        if (deviceCountProperty is null)
        {
            return [];
        }

        var getCapabilitiesMethod = type.GetMethod(
            "GetCapabilities",
            BindingFlags.Public | BindingFlags.Static);
        if (getCapabilitiesMethod is null)
        {
            return [];
        }

        var count = (int)(deviceCountProperty.GetValue(null) ?? 0);
        var devices = new List<HostAudioDevice>();

        for (var index = 0; index < count; index++)
        {
            var capabilities = getCapabilitiesMethod.Invoke(null, [index]);
            if (capabilities is null)
            {
                continue;
            }

            var nameProperty = capabilities.GetType().GetProperty("ProductName");
            var name = nameProperty?.GetValue(capabilities) as string;

            devices.Add(new HostAudioDevice
            {
                DeviceId = $"{prefix}:{index}",
                Name = string.IsNullOrWhiteSpace(name) ? $"{typeName} {index}" : name
            });
        }

        return devices;
    }
    catch
    {
    }

    return [];
}

static IReadOnlyList<HostAudioDevice> ParseHostAlsaDevices(string command, string prefix)
{
    var output = RunHostCommand(command);
    if (string.IsNullOrWhiteSpace(output))
    {
        return [];
    }

    var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    var devices = new List<HostAudioDevice>();
    var regex = new Regex(@"card\s+(\d+):\s+([^:\[\n\r]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    foreach (var line in lines)
    {
        var match = regex.Match(line);
        if (!match.Success)
        {
            continue;
        }

        var cardId = match.Groups[1].Value.Trim();
        var name = match.Groups[2].Value.Trim();
        if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        devices.Add(new HostAudioDevice
        {
            DeviceId = $"{prefix}:{cardId}",
            Name = name
        });
    }

    return devices;
}

static IReadOnlyList<HostAudioDevice> ParseHostMacDevices(bool isRecording)
{
    var output = RunHostCommand("system_profiler SPAudioDataType");
    if (string.IsNullOrWhiteSpace(output))
    {
        return [];
    }

    var lines = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
    var devices = new List<HostAudioDevice>();
    var section = string.Empty;
    var sectionId = isRecording ? "Input" : "Output";
    var regex = new Regex(@"^\s{12}(.+?):$", RegexOptions.Compiled);
    var count = 1;

    foreach (var line in lines)
    {
        if (line.Contains("Input Devices:", StringComparison.Ordinal))
        {
            section = "Input";
            continue;
        }

        if (line.Contains("Output Devices:", StringComparison.Ordinal))
        {
            section = "Output";
            continue;
        }

        if (!string.Equals(section, sectionId, StringComparison.Ordinal))
        {
            continue;
        }

        var match = regex.Match(line);
        if (!match.Success)
        {
            continue;
        }

        var name = match.Groups[1].Value.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        var prefix = isRecording ? "rec" : "play";
        devices.Add(new HostAudioDevice
        {
            DeviceId = $"{prefix}:{count}",
            Name = name
        });
        count++;
    }

    return devices;
}

static string RunHostCommand(string commandLine)
{
    if (string.IsNullOrWhiteSpace(commandLine))
    {
        return string.Empty;
    }

    var splitIndex = commandLine.IndexOf(' ');
    var file = splitIndex >= 0 ? commandLine[..splitIndex] : commandLine;
    var args = splitIndex >= 0 ? commandLine[(splitIndex + 1)..] : string.Empty;

    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        var output = new StringBuilder();
        process.Start();
        output.Append(process.StandardOutput.ReadToEnd());
        process.WaitForExit(500);

        return output.ToString();
    }
    catch
    {
        return string.Empty;
    }
}

static bool IsAuthorized(HttpContext context, RuntimeConfig config, SessionRecord session)
{
    if (config.IsTokenAuthAllowed)
    {
        var token = context.Request.Headers["x-orchestrator-token"].ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return !config.IsTokenAuthRequired;
        }

        if (!string.Equals(token, session.OrchestratorToken, StringComparison.Ordinal))
        {
            return false;
        }
    }

    return true;
}

static void RunHousekeeping(SessionService sessions, SessionEventBus? eventBus, ConcurrentDictionary<string, SessionWorkItem> activeWorkers)
{
    var expired = sessions.CleanupExpiredSessions(DateTimeOffset.UtcNow);
    if (eventBus is null || expired.Count == 0)
    {
        return;
    }

    foreach (var expiredSessionId in expired)
    {
        StopSessionWork(expiredSessionId, activeWorkers);
        eventBus.Complete(expiredSessionId);
    }
}

static void StopSessionWork(string sessionId, ConcurrentDictionary<string, SessionWorkItem> activeWorkers)
{
    if (!activeWorkers.TryGetValue(sessionId, out var workItem))
    {
        return;
    }

    workItem.CancellationTokenSource.Cancel();

    if (!activeWorkers.TryRemove(sessionId, out _))
    {
        return;
    }

    if (workItem.ProcessingTask.IsCompleted)
    {
        workItem.CancellationTokenSource.Dispose();
    }
    else
    {
        _ = workItem.ProcessingTask.ContinueWith(
            _ => workItem.CancellationTokenSource.Dispose(),
            TaskScheduler.Default);
    }
}

static void StartSessionWorkAsync(
    string sessionId,
    string correlationId,
    AudioDeviceSelection? audioDevices,
    SessionService sessions,
    SessionEventBus eventBus,
    ITranscriptionProvider transcriptionProvider,
    TranscriptionDefaultsConfig transcriptionDefaults,
    ConcurrentDictionary<string, SessionWorkItem> activeWorkers)
{
    if (activeWorkers.ContainsKey(sessionId))
    {
        return;
    }

    var workItem = new SessionWorkItem
    {
        CancellationTokenSource = new CancellationTokenSource()
    };

    if (!activeWorkers.TryAdd(sessionId, workItem))
    {
        workItem.CancellationTokenSource.Dispose();
        return;
    }

    workItem.ProcessingTask = ProcessSessionAsync(
        sessionId,
        correlationId,
        audioDevices,
        sessions,
        eventBus,
        transcriptionProvider,
        transcriptionDefaults,
        workItem.CancellationTokenSource.Token);

    _ = workItem.ProcessingTask.ContinueWith(
        _ =>
        {
            FinalizeSessionWork(sessionId, workItem, activeWorkers);
        },
        TaskScheduler.Default);
}

static void FinalizeSessionWork(
    string sessionId,
    SessionWorkItem workItem,
    ConcurrentDictionary<string, SessionWorkItem> activeWorkers)
{
    if (!activeWorkers.TryRemove(sessionId, out var removed))
    {
        return;
    }

    if (!ReferenceEquals(removed, workItem))
    {
        return;
    }

    try
    {
        removed.CancellationTokenSource.Dispose();
    }
    catch
    {
    }
}

static async Task ProcessSessionAsync(
    string sessionId,
    string correlationId,
    AudioDeviceSelection? audioDevices,
    SessionService sessions,
    SessionEventBus eventBus,
    ITranscriptionProvider transcriptionProvider,
    TranscriptionDefaultsConfig transcriptionDefaults,
    CancellationToken ct)
{
    try
    {
        await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = sessionId,
            CorrelationId = correlationId,
            State = SessionState.Running.ToString(),
            Text = "transcribing"
        }, ct);

        if (!await sessions.TryTransitionAsync(
            sessionId,
            state => state is SessionState.Listening,
            SessionState.Running,
            "transcription-started",
            ct))
        {
            return;
        }

        await Task.Delay(250, ct);

        using var audio = new MemoryStream(Array.Empty<byte>());
        var options = new TranscriptionOptions(
            string.IsNullOrWhiteSpace(transcriptionDefaults.DefaultLanguage) ? null : transcriptionDefaults.DefaultLanguage,
            string.IsNullOrWhiteSpace(transcriptionDefaults.DefaultPrompt) ? null : transcriptionDefaults.DefaultPrompt,
            true,
            null);

        var result = await transcriptionProvider.TranscribeAsync(
            audio,
            correlationId,
            options,
            audioDevices,
            ct);
        if (ct.IsCancellationRequested)
        {
            return;
        }

        if (!result.IsSuccess)
        {
            var failed = await sessions.TryTransitionAsync(
                sessionId,
                state => state is SessionState.Running or SessionState.Listening,
                SessionState.Failed,
                "transcription-failed",
                ct);

            if (failed)
            {
                await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
                {
                    EventType = "error",
                    SessionId = sessionId,
                    CorrelationId = correlationId,
                    ErrorCode = result.ErrorCode,
                    ErrorMessage = result.ErrorMessage,
                    Text = "transcription-failed"
                }, ct);
                eventBus.Complete(sessionId);
            }

            return;
        }

        var transitioned = await sessions.TryTransitionAsync(
            sessionId,
            state => state is SessionState.Running,
            SessionState.AwaitingDecision,
            "transcript-ready",
            ct);

        if (!transitioned)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "transcript",
                SessionId = sessionId,
                CorrelationId = correlationId,
                Text = result.Text
            }, ct);
        }

        await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = sessionId,
            CorrelationId = correlationId,
            State = SessionState.AwaitingDecision.ToString(),
            Text = "awaiting-decision"
        }, ct);
    }
    catch (OperationCanceledException)
    {
    }
    catch
    {
        var failed = await sessions.TryTransitionAsync(
            sessionId,
            state => state is SessionState.Running or SessionState.Listening,
            SessionState.Failed,
            "transcription-exception",
            CancellationToken.None);

        if (failed)
        {
            await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
            {
                EventType = "error",
                SessionId = sessionId,
                CorrelationId = correlationId,
                ErrorCode = "INTERNAL_ERROR",
                ErrorMessage = "Unexpected error while processing transcription."
            }, CancellationToken.None);
            eventBus.Complete(sessionId);
        }
    }
    finally
    {
    }
}

static IResult Problem(
    int status,
    string code,
    string detail,
    string? sessionId = null,
    string? correlationId = null) => Results.Json(new ErrorEnvelope
{
    Type = "about:blank",
    Title = code,
    Status = status,
    Detail = detail,
    ErrorCode = code,
    SessionId = sessionId,
    CorrelationId = correlationId
}, statusCode: status);

static async Task WriteProblemResponseAsync(
    HttpContext context,
    int status,
    string code,
    string detail,
    string? sessionId = null,
    string? correlationId = null)
{
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new ErrorEnvelope
    {
        Type = "about:blank",
        Title = code,
        Status = status,
        Detail = detail,
        ErrorCode = code,
        SessionId = sessionId,
        CorrelationId = correlationId
    });
}

static async Task WriteSseEventAsync(HttpContext context, SessionEventEnvelope envelope, CancellationToken ct)
{
    var payload = JsonSerializer.Serialize(envelope);
    await context.Response.WriteAsync($"data: {payload}\n\n", ct);
    await context.Response.Body.FlushAsync(ct);
}

static void PrintUsage()
{
    Console.WriteLine("VoiceType2 API Host (Alpha 1)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project VoiceType2.ApiHost/VoiceType2.ApiHost.csproj -- --mode service [--urls <url>] [--config <path>] [--help]");
}
