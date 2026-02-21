using System.Text.Json;
using VoiceType2.App.Cli;
using VoiceType2.Core.Contracts;

var input = ParseArguments(args);
var command = input.Command;

var apiUrl = input.GetFlagValue("--api-url") ?? "http://127.0.0.1:5240";
var apiToken = input.GetFlagValue("--api-token");
var sessionId = input.GetFlagValue("--session-id");

var exitCode = command switch
{
    "run" => await RunAsync(apiUrl, apiToken),
    "status" => await StatusAsync(apiUrl, sessionId, apiToken),
    "stop" => await StopAsync(apiUrl, sessionId, apiToken),
    "resolve" => await ResolveAsync(apiUrl, sessionId, input.PositionalArgs, apiToken),
    "api" => await ApiAsync(apiUrl),
    _ => PrintUsage(),
};

Environment.ExitCode = exitCode;

return;

static async Task<int> RunAsync(string apiUrl, string? apiToken)
{
    await using var bootstrapClient = new ApiSessionClient(apiUrl);
    if (!await bootstrapClient.IsReadyAsync())
    {
        Console.Error.WriteLine("API host is not reachable.");
        return 1;
    }

    var profile = CreateProfile();
    var created = await bootstrapClient.RegisterAsync(profile, "dictate");
    await using var sessionClient = new ApiSessionClient(apiUrl, created.OrchestratorToken);

    await sessionClient.StartAsync(created.SessionId);
    Console.WriteLine($"Session started: {created.SessionId}");
    Console.WriteLine($"sessionId={created.SessionId} state={created.State}");
    Console.WriteLine("Controls: [s]ubmit, [c]ancel, [r]etry, [q]uit, status, resolve <action>");

    using var eventCts = new CancellationTokenSource();
    var eventLoop = PrintEventsAsync(sessionClient, created.SessionId, eventCts.Token);

    while (!eventCts.Token.IsCancellationRequested)
    {
        var line = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var normalized = line.Trim().ToLowerInvariant();
        if (normalized is "q" or "quit" or "exit")
        {
            break;
        }

        if (normalized is "s" or "submit")
        {
            await sessionClient.ResolveAsync(created.SessionId, "submit");
            continue;
        }

        if (normalized is "c" or "cancel")
        {
            await sessionClient.ResolveAsync(created.SessionId, "cancel");
            continue;
        }

        if (normalized is "r" or "retry")
        {
            await sessionClient.ResolveAsync(created.SessionId, "retry");
            continue;
        }

        if (normalized == "status")
        {
            var status = await sessionClient.GetStatusAsync(created.SessionId);
            Console.WriteLine($"status state={status.State} lastEvent={status.LastEvent}");
            continue;
        }

        if (normalized is "h" or "help")
        {
            Console.WriteLine("Enter s/submit, c/cancel, r/retry, status, q/quit.");
            continue;
        }

        Console.WriteLine("Unknown command. Type 'help'.");
    }

    eventCts.Cancel();
    await sessionClient.StopAsync(created.SessionId);
    await eventLoop;
    return 0;
}

static async Task<int> StatusAsync(string apiUrl, string? sessionId, string? apiToken)
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        Console.Error.WriteLine("Missing --session-id.");
        return 1;
    }

    await using var client = new ApiSessionClient(apiUrl, apiToken);
    var status = await client.GetStatusAsync(sessionId);
    Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return 0;
}

static async Task<int> StopAsync(string apiUrl, string? sessionId, string? apiToken)
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        Console.Error.WriteLine("Missing --session-id.");
        return 1;
    }

    await using var client = new ApiSessionClient(apiUrl, apiToken);
    await client.StopAsync(sessionId);
    Console.WriteLine("Stop request sent.");
    return 0;
}

static async Task<int> ResolveAsync(string apiUrl, string? sessionId, string[] positionalArgs, string? apiToken)
{
    var action = positionalArgs.FirstOrDefault()?.ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(action) || string.IsNullOrWhiteSpace(sessionId))
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Console.Error.WriteLine("Missing --session-id.");
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            Console.Error.WriteLine("Missing action. Use submit|cancel|retry.");
        }

        return 1;
    }

    if (!TryNormalizeAction(action, out var normalized))
    {
        Console.Error.WriteLine($"Unsupported action '{action}'.");
        return 1;
    }

    await using var client = new ApiSessionClient(apiUrl, apiToken);
    await client.ResolveAsync(sessionId, normalized);
    Console.WriteLine($"Resolve '{normalized}' sent for {sessionId}.");
    return 0;
}

static async Task<int> ApiAsync(string apiUrl)
{
    await using var client = new ApiSessionClient(apiUrl);
    var ready = await client.IsReadyAsync();
    Console.WriteLine($"ready={ready}");
    return ready ? 0 : 1;
}

static async Task PrintEventsAsync(ApiSessionClient client, string sessionId, CancellationToken ct)
{
    try
    {
        await foreach (var evt in client.StreamEventsAsync(sessionId, ct))
        {
            var text = evt.Text;
            var state = evt.State;
            var line = evt.EventType;
            if (!string.IsNullOrWhiteSpace(state))
            {
                line += $": {state}";
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                line += $" - {text}";
            }

            Console.WriteLine(line);
        }
    }
    catch (OperationCanceledException)
    {
    }
}

static OrchestratorProfile CreateProfile()
{
    var platform = OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "macos"
            : "linux";

    return new OrchestratorProfile
    {
        OrchestratorId = "cli-orchestrator",
        Platform = platform,
        Capabilities = new OrchestratorCapabilities(
            hotkeys: false,
            tray: false,
            clipboard: true,
            notifications: false,
            audioCapture: false,
            uiShell: false)
    };
}

static int PrintUsage()
{
    Console.WriteLine("VoiceType2 CLI (Alpha 1)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  vt2 run [--api-url <url>] [--mode attach|managed] [--api-token <token>]");
    Console.WriteLine("  vt2 status --session-id <id> [--api-url <url>]");
    Console.WriteLine("  vt2 stop --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 resolve <submit|cancel|retry> --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 api");
    return 1;
}

static bool TryNormalizeAction(string action, out string normalized)
{
    normalized = action.Trim().ToLowerInvariant() switch
    {
        "submit" => "submit",
        "s" => "submit",
        "cancel" => "cancel",
        "c" => "cancel",
        "retry" => "retry",
        "r" => "retry",
        _ => string.Empty
    };

    return !string.IsNullOrWhiteSpace(normalized);
}

static ParsedArguments ParseArguments(string[] args)
{
    var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var positional = new List<string>();

    var i = 0;
    string? firstNonFlag = null;

    while (i < args.Length)
    {
        var current = args[i];
        if (current.StartsWith("--", StringComparison.Ordinal))
        {
            var currentName = current;
            var nextValue = "true";
            if (current.Contains('='))
            {
                var split = currentName.Split('=', 2, StringSplitOptions.TrimEntries);
                currentName = split[0];
                nextValue = split[1];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                nextValue = args[i + 1];
                i++;
            }

            flags[currentName.ToLowerInvariant()] = nextValue;
        }
        else if (firstNonFlag is null)
        {
            firstNonFlag = current;
        }
        else
        {
            positional.Add(current);
        }

        i++;
    }

    return new ParsedArguments(
        (firstNonFlag ?? "run").ToLowerInvariant(),
        positional.ToArray(),
        flags);
}

internal sealed record ParsedArguments(string Command, string[] PositionalArgs, Dictionary<string, string> Flags)
{
    public string? GetFlagValue(string key)
    {
        return Flags.TryGetValue(key, out var value) ? value : null;
    }
}
