using System.Text.Json;
using VoiceType2.Alpha2.ApiHost;
using VoiceType2.Alpha2.ApiHost.Services;
using VoiceType2.Alpha2.Core;
using VoiceType2.Alpha2.Infrastructure;

var options = ApiHostOptions.Parse(args);
if (options.ShowHelp)
{
    PrintUsage();
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
builder.Logging.AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ");

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<SessionEventHub>();
builder.Services.AddSingleton<SessionCoordinator>();

if (string.Equals(config.AudioCapture.Source, "wave-in", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAudioInputSource, WaveInAudioInputSource>();
}
else
{
    builder.Services.AddSingleton<IAudioInputSource, FakeAudioInputSource>();
}

if (string.Equals(config.Transcription.Provider, "openai", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<ITranscriptionProvider>(sp =>
        new OpenAiTranscriptionProvider(
            config.Transcription.OpenAiApiKeyEnvVar,
            sp.GetRequiredService<ILogger<OpenAiTranscriptionProvider>>()));
}
else
{
    builder.Services.AddSingleton<ITranscriptionProvider, MockTranscriptionProvider>();
}

builder.WebHost.UseUrls(config.HostBinding.Urls);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new
{
    status = "live",
    timestamp = DateTimeOffset.UtcNow
}));

app.MapGet("/health/ready", () => Results.Ok(new
{
    status = "ready",
    audioSource = config.AudioCapture.Source,
    transcriptionProvider = config.Transcription.Provider,
    timestamp = DateTimeOffset.UtcNow
}));

app.MapPost("/v2/sessions", (RegisterSessionRequest request, SessionCoordinator coordinator) =>
{
    var created = coordinator.Register(request);
    return Results.Ok(created);
});

app.MapGet("/v2/sessions/{sessionId}", (string sessionId, HttpContext context, SessionCoordinator coordinator) =>
{
    try
    {
        var status = coordinator.GetStatus(sessionId, context.Request.Headers["x-orchestrator-token"]);
        return Results.Ok(status);
    }
    catch (SessionCoordinatorException ex)
    {
        return Problem(sessionId, null, ex);
    }
});

app.MapPost("/v2/sessions/{sessionId}/start", async (
    string sessionId,
    HttpContext context,
    SessionCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    try
    {
        var status = await coordinator.StartAsync(sessionId, context.Request.Headers["x-orchestrator-token"], cancellationToken);
        return Results.Ok(status);
    }
    catch (SessionCoordinatorException ex)
    {
        return Problem(sessionId, null, ex);
    }
});

app.MapPost("/v2/sessions/{sessionId}/stop", async (
    string sessionId,
    HttpContext context,
    SessionCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    try
    {
        var status = await coordinator.StopAsync(sessionId, context.Request.Headers["x-orchestrator-token"], cancellationToken);
        return Results.Ok(status);
    }
    catch (SessionCoordinatorException ex)
    {
        return Problem(sessionId, null, ex);
    }
});

app.MapPost("/v2/sessions/{sessionId}/cancel", async (
    string sessionId,
    HttpContext context,
    SessionCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    try
    {
        var status = await coordinator.CancelAsync(sessionId, context.Request.Headers["x-orchestrator-token"], cancellationToken);
        return Results.Ok(status);
    }
    catch (SessionCoordinatorException ex)
    {
        return Problem(sessionId, null, ex);
    }
});

app.MapGet("/v2/sessions/{sessionId}/events", async (
    string sessionId,
    HttpContext context,
    SessionCoordinator coordinator,
    SessionEventHub eventHub,
    CancellationToken cancellationToken) =>
{
    try
    {
        var session = coordinator.GetAuthorizedSession(sessionId, context.Request.Headers["x-orchestrator-token"]);
        context.Response.Headers["Content-Type"] = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no";

        await WriteSseEventAsync(context, new SessionEventEnvelope
        {
            EventType = "status",
            SessionId = sessionId,
            CorrelationId = session.CorrelationId,
            State = session.State.ToString(),
            Text = "stream-opened"
        }, cancellationToken);

        await foreach (var evt in eventHub.SubscribeAsync(sessionId, cancellationToken))
        {
            await WriteSseEventAsync(context, evt, cancellationToken);
        }
    }
    catch (SessionCoordinatorException ex)
    {
        await WriteProblemResponseAsync(context, sessionId, null, ex);
    }
});

await app.RunAsync();

static IResult Problem(string? sessionId, string? correlationId, SessionCoordinatorException ex)
{
    return Results.Json(new ErrorEnvelope
    {
        Type = "about:blank",
        Title = ex.ErrorCode,
        Status = ex.StatusCode,
        Detail = ex.Message,
        ErrorCode = ex.ErrorCode,
        SessionId = sessionId,
        CorrelationId = correlationId
    }, statusCode: ex.StatusCode);
}

static async Task WriteProblemResponseAsync(
    HttpContext context,
    string? sessionId,
    string? correlationId,
    SessionCoordinatorException ex)
{
    context.Response.StatusCode = ex.StatusCode;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new ErrorEnvelope
    {
        Type = "about:blank",
        Title = ex.ErrorCode,
        Status = ex.StatusCode,
        Detail = ex.Message,
        ErrorCode = ex.ErrorCode,
        SessionId = sessionId,
        CorrelationId = correlationId
    });
}

static async Task WriteSseEventAsync(HttpContext context, SessionEventEnvelope envelope, CancellationToken cancellationToken)
{
    var payload = JsonSerializer.Serialize(envelope);
    await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
    await context.Response.Body.FlushAsync(cancellationToken);
}

static void PrintUsage()
{
    Console.WriteLine("VoiceType2 Alpha 2 API Host");
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project VoiceType2.Alpha2.ApiHost.csproj -- [--urls <url>] [--config <path>] [--help]");
}

public sealed class ApiHostOptions
{
    private ApiHostOptions(string? urls, string? configPath, bool showHelp)
    {
        Urls = urls;
        ConfigPath = configPath;
        ShowHelp = showHelp;
    }

    public string? Urls { get; }
    public string? ConfigPath { get; }
    public bool ShowHelp { get; }

    public static ApiHostOptions Parse(string[] args)
    {
        string? urls = null;
        string? configPath = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
            }
            else if (string.Equals(current, "--urls", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                urls = args[++i];
            }
            else if (string.Equals(current, "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
        }

        return new ApiHostOptions(urls, configPath, showHelp);
    }
}

public sealed partial class Program;
