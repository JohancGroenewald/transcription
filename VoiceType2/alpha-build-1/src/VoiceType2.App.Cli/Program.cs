using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;
using VoiceType2.App.Cli;
using VoiceType2.Core.Contracts;

var input = ParseArguments(args);
var command = input.Command;

var apiUrl = input.GetFlagValue("--api-url") ?? "http://127.0.0.1:5240";
var apiToken = input.GetFlagValue("--api-token");
var sessionId = input.GetFlagValue("--session-id");
var mode = input.GetFlagValue("--mode") ?? "attach";
var managedApiConfig = input.GetFlagValue("--api-config");
var apiTimeoutMs = ParseInt(input.GetFlagValue("--api-timeout-ms"), 15000);
var shutdownTimeoutMs = ParseInt(input.GetFlagValue("--shutdown-timeout-ms"), 10000);
var managedStart = ParseBool(input.GetFlagValue("--managed-start"), true);
var showHelp = input.Flags.ContainsKey("--help") || input.Flags.ContainsKey("-h");

if (showHelp)
{
    PrintUsage();
    return;
}

if (command == "run" && input.Flags.ContainsKey("--tui"))
{
    command = "tui";
}

var exitCode = command switch
{
    "run" => await RunAsync(apiUrl, mode, managedStart, managedApiConfig, apiTimeoutMs, shutdownTimeoutMs),
    "tui" => await TuiAsync(apiUrl, mode, managedStart, managedApiConfig, apiTimeoutMs, shutdownTimeoutMs),
    "status" => await StatusAsync(apiUrl, sessionId, apiToken),
    "stop" => await StopAsync(apiUrl, sessionId, apiToken),
    "resolve" => await ResolveAsync(apiUrl, sessionId, input.PositionalArgs, apiToken),
    "api" => await ApiAsync(apiUrl, input.PositionalArgs),
    _ => PrintUsage(),
};

Environment.ExitCode = exitCode;

return;

static async Task<int> RunAsync(
    string apiUrl,
    string mode,
    bool managedStart,
    string? managedApiConfig,
    int apiTimeoutMs,
    int shutdownTimeoutMs)
{
    Process? managedProcess = null;
    try
    {
        if (!await EnsureApiReadyAsync(apiUrl, apiTimeoutMs))
        {
            if (!string.Equals(mode, "managed", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("API host is not reachable. Use --mode managed to auto-start it.");
                return 1;
            }

            if (!managedStart)
            {
                Console.Error.WriteLine("Managed start is disabled. Cannot start API host.");
                return 1;
            }

            managedProcess = StartManagedApi(apiUrl, managedApiConfig);
            if (managedProcess is null)
            {
                return 1;
            }

            if (!await EnsureApiReadyAsync(apiUrl, apiTimeoutMs))
            {
                Console.Error.WriteLine("Managed API host did not become ready.");
                return 1;
            }
        }

        await using var bootstrapClient = new ApiSessionClient(apiUrl);
        var profile = CreateProfile();
        var created = await bootstrapClient.RegisterAsync(profile, "dictate");
        await using var sessionClient = new ApiSessionClient(apiUrl, created.OrchestratorToken);

        await sessionClient.StartAsync(created.SessionId);
        PrintSessionStartedHeader(created.SessionId, created.State, created.CorrelationId, apiUrl, mode);
        PrintRunMenu();

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
                Console.WriteLine("Stopping session and exiting...");
                break;
            }

            if (normalized is "s" or "submit")
            {
                await sessionClient.ResolveAsync(created.SessionId, "submit");
                Console.WriteLine("Action sent: submit");
                continue;
            }

            if (normalized is "c" or "cancel")
            {
                await sessionClient.ResolveAsync(created.SessionId, "cancel");
                Console.WriteLine("Action sent: cancel");
                continue;
            }

            if (normalized is "r" or "retry")
            {
                await sessionClient.ResolveAsync(created.SessionId, "retry");
                Console.WriteLine("Action sent: retry");
                continue;
            }

            if (normalized == "status")
            {
                await PrintSessionStatusAsync(sessionClient, created.SessionId);
                continue;
            }

            if (normalized is "h" or "help" or "menu")
            {
                PrintRunMenu();
                continue;
            }

            Console.WriteLine("Unknown command. Type 'help' or 'menu'.");
        }

        eventCts.Cancel();
        try
        {
            await sessionClient.StopAsync(created.SessionId);
        }
        catch (ApiHostException ex) when (ex.StatusCode is 409 or 404)
        {
        }

        await eventLoop;
        return 0;
    }
    finally
    {
        if (managedProcess is not null && !managedProcess.HasExited)
        {
            StopManagedApi(managedProcess, shutdownTimeoutMs);
        }
    }
}

static async Task<int> TuiAsync(
    string apiUrl,
    string mode,
    bool managedStart,
    string? managedApiConfig,
    int apiTimeoutMs,
    int shutdownTimeoutMs)
{
    Process? managedProcess = null;
    try
    {
        if (!await EnsureApiReadyAsync(apiUrl, apiTimeoutMs))
        {
            if (!string.Equals(mode, "managed", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine("[red]API host is not reachable. Use --mode managed to auto-start it.[/]");
                return 1;
            }

            if (!managedStart)
            {
                AnsiConsole.MarkupLine("[red]Managed start is disabled. Cannot start API host.[/]");
                return 1;
            }

            managedProcess = StartManagedApi(apiUrl, managedApiConfig);
            if (managedProcess is null)
            {
                return 1;
            }

            if (!await EnsureApiReadyAsync(apiUrl, apiTimeoutMs))
            {
                AnsiConsole.MarkupLine("[red]Managed API host did not become ready.[/]");
                return 1;
            }
        }

        await using var bootstrapClient = new ApiSessionClient(apiUrl);
        var profile = CreateProfile();
        var created = await bootstrapClient.RegisterAsync(profile, "dictate");
        await using var sessionClient = new ApiSessionClient(apiUrl, created.OrchestratorToken);

        await sessionClient.StartAsync(created.SessionId);

        AnsiConsole.Clear();
        AnsiConsole.Write(
            new FigletText("VoiceType2")
                .Centered()
                .Color(Color.DeepSkyBlue3));

        AnsiConsole.Write(
            new Panel(
                $"Session: [yellow]{created.SessionId}[/]\nMode: [green]{mode}[/]\nState: [blue]{created.State}[/]\nCorrelation: [grey]{created.CorrelationId}[/]\nAPI: [blue]{apiUrl}[/]")
            {
                Header = new PanelHeader("TUI Session")
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Session event stream will appear below.[/]");

        using var eventCts = new CancellationTokenSource();
        _ = PrintTuiEventsAsync(sessionClient, created.SessionId, eventCts.Token);

        while (true)
        {
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Choose an action")
                    .AddChoices("submit", "cancel", "retry", "status", "help", "quit"));

            if (selected is "quit")
            {
                break;
            }

            if (selected is "help")
            {
                PrintTuiMenu();
                continue;
            }

            if (selected is "status")
            {
                await PrintSessionStatusAsync(sessionClient, created.SessionId);
                continue;
            }

            var normalized = selected switch
            {
                "submit" => "submit",
                "cancel" => "cancel",
                "retry" => "retry",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(normalized))
            {
                AnsiConsole.MarkupLine("[red]Unknown action.[/]");
                continue;
            }

            await sessionClient.ResolveAsync(created.SessionId, normalized);
            AnsiConsole.MarkupLine($"[green]Sent action:[/] {normalized}");
        }

        eventCts.Cancel();
        try
        {
            await sessionClient.StopAsync(created.SessionId);
        }
        catch (ApiHostException ex) when (ex.StatusCode is 409 or 404)
        {
        }

        return 0;
    }
    finally
    {
        if (managedProcess is not null && !managedProcess.HasExited)
        {
            StopManagedApi(managedProcess, shutdownTimeoutMs);
        }
    }
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

static async Task<int> ApiAsync(string apiUrl, string[] positionalArgs)
{
    var subcommand = positionalArgs.FirstOrDefault()?.ToLowerInvariant();
    if (subcommand is "status" or null or "")
    {
        await using var client = new ApiSessionClient(apiUrl);
        var ready = await client.IsReadyAsync();
        Console.WriteLine($"ready={ready}");
        return ready ? 0 : 1;
    }

    if (subcommand is "help")
    {
        Console.WriteLine("vt2 api [status]");
        return 0;
    }

    Console.Error.WriteLine($"Unknown api command '{subcommand}'.");
    return 1;
}

static async Task PrintEventsAsync(ApiSessionClient client, string sessionId, CancellationToken ct)
{
    try
    {
        await foreach (var evt in client.StreamEventsAsync(sessionId, ct))
        {
            PrintSessionEvent(evt);
        }
    }
    catch (OperationCanceledException)
    {
    }
}

static async Task PrintTuiEventsAsync(ApiSessionClient client, string sessionId, CancellationToken ct)
{
    try
    {
        await foreach (var evt in client.StreamEventsAsync(sessionId, ct))
        {
            PrintSessionEventForTui(evt);
        }
    }
    catch (OperationCanceledException)
    {
    }
}

static void PrintSessionStartedHeader(string sessionId, string state, string correlationId, string apiUrl, string mode)
{
    Console.WriteLine();
    Console.WriteLine("=== VoiceType2 Dictation Session ===");
    Console.WriteLine($"Mode:        {mode}");
    Console.WriteLine($"API URL:     {apiUrl}");
    Console.WriteLine($"Session ID:  {sessionId}");
    Console.WriteLine($"State:       {state}");
    Console.WriteLine($"Correlation: {correlationId}");
    Console.WriteLine("=================================");
    Console.WriteLine();
}

static void PrintRunMenu()
{
    Console.WriteLine("Session menu (enter a command and press Enter):");
    Console.WriteLine("  1) submit  (s)  - Accept transcript and complete");
    Console.WriteLine("  2) cancel  (c)  - Cancel current transcript");
    Console.WriteLine("  3) retry   (r)  - Retry transcription");
    Console.WriteLine("  4) status       - Show current status");
    Console.WriteLine("  5) quit    (q)  - Stop session and exit");
    Console.WriteLine("  6) help    (h)  - Show this menu again");
    Console.WriteLine("=================================");
}

static void PrintTuiMenu()
{
    AnsiConsole.Write(
        new Table
        {
            Border = TableBorder.Rounded,
            Width = 48
        }
        .AddColumn("Command")
        .AddColumn("Description")
        .AddRow("[green]submit[/]", "Accept transcript and complete")
        .AddRow("[green]cancel[/]", "Cancel current transcript")
        .AddRow("[green]retry[/]", "Retry transcription")
        .AddRow("[green]status[/]", "Show current status")
        .AddRow("[green]help[/]", "Show this menu")
        .AddRow("[green]quit[/]", "Stop session and exit"));
}

static async Task PrintSessionStatusAsync(ApiSessionClient client, string sessionId)
{
    var status = await client.GetStatusAsync(sessionId);
    Console.WriteLine(
        $"sessionId={status.SessionId} state={status.State} " +
        $"lastEvent={status.LastEvent} correlationId={status.CorrelationId} revision={status.Revision}");
}

static void PrintSessionEvent(SessionEventEnvelope evt)
{
    var category = evt.EventType switch
    {
        "status" => "[STATUS]",
        "transcript" => "[TRANSCRIPT]",
        "command" => "[COMMAND]",
        "error" => "[ERROR]",
        _ => "[EVENT]"
    };

    var details = new List<string>();
    if (!string.IsNullOrWhiteSpace(evt.State))
    {
        details.Add($"state={evt.State}");
    }

    if (!string.IsNullOrWhiteSpace(evt.Text))
    {
        details.Add($"text={evt.Text}");
    }

    if (!string.IsNullOrWhiteSpace(evt.ErrorCode))
    {
        details.Add($"code={evt.ErrorCode}");
    }

    if (!string.IsNullOrWhiteSpace(evt.ErrorMessage))
    {
        details.Add($"error={evt.ErrorMessage}");
    }

    if (details.Count == 0)
    {
        Console.WriteLine($"{category} correlationId={evt.CorrelationId}");
        return;
    }

    Console.WriteLine($"{category} {string.Join(" | ", details)}");
}

static void PrintSessionEventForTui(SessionEventEnvelope evt)
{
    var color = evt.EventType switch
    {
        "status" => "blue",
        "transcript" => "green",
        "command" => "yellow",
        "error" => "red",
        _ => "grey"
    };

    var details = new List<string>();
    if (!string.IsNullOrWhiteSpace(evt.State))
    {
        details.Add($"state={evt.State}");
    }

    if (!string.IsNullOrWhiteSpace(evt.Text))
    {
        details.Add($"text={evt.Text}");
    }

    if (!string.IsNullOrWhiteSpace(evt.ErrorCode))
    {
        details.Add($"code={evt.ErrorCode}");
    }

    if (!string.IsNullOrWhiteSpace(evt.ErrorMessage))
    {
        details.Add($"error={evt.ErrorMessage}");
    }

    if (details.Count == 0)
    {
        AnsiConsole.MarkupLine($"[{color}][{evt.EventType}][/]: correlationId={evt.CorrelationId}");
    }
    else
    {
        AnsiConsole.MarkupLine($"[{color}][{evt.EventType}][/]: {Markup.Escape(string.Join(" | ", details))}");
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

static async Task<bool> EnsureApiReadyAsync(string apiUrl, int timeoutMs)
{
    await using var client = new ApiSessionClient(apiUrl);
    try
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        while (!timeout.Token.IsCancellationRequested)
        {
            try
            {
                if (await client.IsReadyAsync(timeout.Token))
                {
                    return true;
                }
            }
            catch
            {
                // Retry until timeout.
            }

            await Task.Delay(250, timeout.Token);
        }
    }
    catch
    {
    }

    return false;
}

static Process? StartManagedApi(string apiUrl, string? configPath)
{
    try
    {
        var projectPath = FindApiHostProjectPath();
        var workingDir = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(workingDir))
        {
            throw new InvalidOperationException("Invalid API project path.");
        }

        var arguments = new List<string>
        {
            "run",
            "--project",
            Quote(projectPath),
            "--",
            "--mode",
            "service",
            "--urls",
            Quote(apiUrl)
        };

        if (!string.IsNullOrWhiteSpace(configPath))
        {
            arguments.Add("--config");
            arguments.Add(Quote(configPath));
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.Join(' ', arguments),
                WorkingDirectory = workingDir,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        return process;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to start managed API host: {ex.Message}");
        return null;
    }
}

static void StopManagedApi(Process process, int timeoutMs)
{
    try
    {
        if (!process.HasExited)
        {
            process.CloseMainWindow();
            if (!process.WaitForExit(Math.Max(200, timeoutMs / 2)))
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(timeoutMs);
        }
    }
    catch
    {
    }
    finally
    {
        process.Dispose();
    }
}

static string FindApiHostProjectPath()
{
    var target = Path.Combine("VoiceType2", "alpha-build-1", "src", "VoiceType2.ApiHost", "VoiceType2.ApiHost.csproj");
    var candidateRoots = new List<string>
    {
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory
    };

    foreach (var root in candidateRoots)
    {
        var current = root;
        for (var depth = 0; depth < 10 && current is not null; depth++)
        {
            var candidate = Path.GetFullPath(Path.Combine(current, target));
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
            if (current is null)
            {
                break;
            }
        }
    }

    throw new FileNotFoundException("Could not resolve VoiceType2.ApiHost project path.");
}

static string Quote(string value)
{
    return $"\"{value}\"";
}

static bool ParseBool(string? value, bool defaultValue)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "on" => true,
        "false" or "0" or "no" or "off" => false,
        _ => defaultValue
    };
}

static int ParseInt(string? value, int defaultValue)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return int.TryParse(value, out var parsed) && parsed > 0
        ? parsed
        : defaultValue;
}

static int PrintUsage()
{
    Console.WriteLine("VoiceType2 CLI (Alpha 1)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  vt2 run [--api-url <url>] [--mode attach|managed] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--api-config <path>]");
    Console.WriteLine("  vt2 tui [--api-url <url>] [--mode attach|managed] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--api-config <path>]");
    Console.WriteLine("  vt2 --tui [--mode attach|managed] [--api-url <url>] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--api-config <path>]");
    Console.WriteLine("  vt2 status --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 stop --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 resolve <submit|cancel|retry> --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 api [status]");
    return 1;
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
