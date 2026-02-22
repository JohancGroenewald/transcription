using System.Text.Json;
using VoiceType2.ApiHost.Services;
using VoiceType2.Core.Contracts;

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
    config = options.ConfigPath is null
        ? new RuntimeConfig()
        : RuntimeConfig.Load(options.ConfigPath);
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
builder.Services.AddSingleton<SessionService>();
builder.Services.AddSingleton<SessionEventBus>();

builder.WebHost.UseUrls(config.HostBinding.Urls);

var app = builder.Build();

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
    RunHousekeeping(sessions, eventBus);
    request ??= new RegisterSessionRequest();
    var requestCorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId)
        ? $"corr-{Guid.NewGuid():N}"
        : request.CorrelationId;

    try
    {
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

app.MapGet("/v1/sessions/{sessionId}", (string sessionId, HttpContext context, SessionService sessions) =>
{
    RunHousekeeping(sessions, null);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, session))
    {
        return Problem(401, "INVALID_TOKEN", "Missing or invalid orchestrator token.");
    }

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = session.SessionId,
        State = session.State.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = session.LastEvent,
        Revision = session.Revision
    });
});

app.MapPost("/v1/sessions/{sessionId}/start", async (
    string sessionId,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, session))
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
            LastEvent = "already-listening"
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

    _ = SimulateTranscriptionAsync(sessionId, session.CorrelationId, sessions, eventBus, ct);

    return Results.Ok(new SessionStatusResponse
    {
        SessionId = sessionId,
        State = SessionState.Listening.ToString(),
        CorrelationId = session.CorrelationId,
        LastEvent = "started"
    });
});

app.MapPost("/v1/sessions/{sessionId}/stop", async (
    string sessionId,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, session))
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
            LastEvent = session.LastEvent
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
        LastEvent = "stopped"
    });
});

app.MapPost("/v1/sessions/{sessionId}/resolve", async (
    string sessionId,
    ResolveRequest request,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus);
    if (!sessions.TryGet(sessionId, out var session))
    {
        return Problem(404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
    }

    if (!IsAuthorized(context, session))
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
        _ = SimulateTranscriptionAsync(sessionId, session.CorrelationId, sessions, eventBus, ct);
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
        LastEvent = $"resolve:{action}"
    });
});

app.MapGet("/v1/sessions/{sessionId}/events", async (
    string sessionId,
    HttpContext context,
    SessionService sessions,
    SessionEventBus eventBus,
    CancellationToken ct) =>
{
    RunHousekeeping(sessions, eventBus);
    if (!sessions.TryGet(sessionId, out var session))
    {
        await WriteProblemResponseAsync(context, 404, "SESSION_NOT_FOUND", "Session not found.", sessionId);
        return;
    }

    if (!IsAuthorized(context, session))
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

static bool IsAuthorized(HttpContext context, SessionRecord session)
{
    var token = context.Request.Headers["x-orchestrator-token"].ToString();
    if (string.IsNullOrWhiteSpace(token))
    {
        return false;
    }

    return string.Equals(token, session.OrchestratorToken, StringComparison.Ordinal);
}

static void RunHousekeeping(SessionService sessions, SessionEventBus? eventBus)
{
    var expired = sessions.CleanupExpiredSessions(DateTimeOffset.UtcNow);
    if (eventBus is null || expired.Count == 0)
    {
        return;
    }

    foreach (var expiredSessionId in expired)
    {
        eventBus.Complete(expiredSessionId);
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

static async Task SimulateTranscriptionAsync(
    string sessionId,
    string correlationId,
    SessionService sessions,
    SessionEventBus eventBus,
    CancellationToken ct)
{
    try
    {
        await Task.Delay(500, ct);
        await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = sessionId,
            CorrelationId = correlationId,
            State = SessionState.Running.ToString(),
            Text = "transcribing"
        }, ct);

        await Task.Delay(900, ct);
        if (!sessions.TryGet(sessionId, out var current))
        {
            return;
        }

        if (current.State != SessionState.Listening)
        {
            return;
        }

        await eventBus.PublishAsync(sessionId, new SessionEventEnvelope
        {
            EventType = "transcript",
            SessionId = sessionId,
            CorrelationId = correlationId,
            Text = "alpha transcript sample for validation"
        }, ct);

        var transitioned = await sessions.TryTransitionAsync(
            sessionId,
            state => state is SessionState.Listening,
            SessionState.AwaitingDecision,
            "transcript-ready",
            ct);

        if (!transitioned)
        {
            return;
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
}

static void PrintUsage()
{
    Console.WriteLine("VoiceType2 API Host (Alpha 1)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project VoiceType2.ApiHost/VoiceType2.ApiHost.csproj -- --mode service [--urls <url>] [--config <path>] [--help]");
}

public sealed class ApiHostOptions
{
    private ApiHostOptions(string mode, string? urls, string? configPath, bool showHelp)
    {
        Mode = mode;
        Urls = urls;
        ConfigPath = configPath;
        ShowHelp = showHelp;
    }

    public string Mode { get; }
    public string? Urls { get; }
    public string? ConfigPath { get; }
    public bool ShowHelp { get; }

    public static ApiHostOptions Parse(string[] args)
    {
        var mode = "service";
        string? urls = null;
        string? configPath = null;
        var showHelp = false;

        var i = 0;
        while (i < args.Length)
        {
            var current = args[i];
            if (string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else if (current.Equals("--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    mode = args[i + 1];
                    i++;
                }
            }
            else if (current.Equals("--urls", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    urls = args[i + 1];
                    i++;
                }
            }
            else if (current.Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    configPath = args[i + 1];
                    i++;
                }
            }

            i++;
        }

        return new ApiHostOptions(mode, urls, configPath, showHelp);
    }
}

public sealed partial class Program;
