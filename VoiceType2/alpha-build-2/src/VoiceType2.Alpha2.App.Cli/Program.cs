using System.Diagnostics;
using System.Text.Json;
using VoiceType2.Alpha2.App.Cli;
using VoiceType2.Alpha2.Core;

var input = ParseArguments(args);
if (input.ShowHelp)
{
    Environment.ExitCode = PrintUsage();
    return;
}

var apiUrl = input.GetFlagValue("--api-url") ?? "http://127.0.0.1:5250";
var apiTimeoutMs = ParsePositiveInt(input.GetFlagValue("--api-timeout-ms"), 15000);
var shutdownTimeoutMs = ParsePositiveInt(input.GetFlagValue("--shutdown-timeout-ms"), 10000);
var mode = input.GetFlagValue("--mode") ?? "attach";
var apiConfig = input.GetFlagValue("--api-config");
var sessionId = input.GetFlagValue("--session-id");
var apiToken = input.GetFlagValue("--api-token");

var exitCode = input.Command switch
{
    "run" => await RunAsync(apiUrl, mode, apiConfig, apiTimeoutMs, shutdownTimeoutMs),
    "status" => await StatusAsync(apiUrl, sessionId, apiToken),
    "cancel" => await CancelAsync(apiUrl, sessionId, apiToken),
    "api" => await ApiAsync(apiUrl),
    _ => PrintUsage()
};

Environment.ExitCode = exitCode;

static async Task<int> RunAsync(
    string apiUrl,
    string mode,
    string? apiConfig,
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

            managedProcess = StartManagedApi(apiUrl, apiConfig);
            if (managedProcess is null || !await EnsureApiReadyAsync(apiUrl, apiTimeoutMs))
            {
                Console.Error.WriteLine("Managed API host did not become ready.");
                return 1;
            }
        }

        await using var bootstrapClient = new ApiSessionClient(apiUrl);
        var created = await bootstrapClient.RegisterAsync(CreateProfile());
        await using var sessionClient = new ApiSessionClient(apiUrl, created.OrchestratorToken);

        using var eventsCts = new CancellationTokenSource();
        var eventLoop = PrintEventsAsync(sessionClient, created.SessionId, eventsCts.Token);

        var started = await sessionClient.StartAsync(created.SessionId);
        Console.WriteLine($"Session started: {started.SessionId}");
        Console.WriteLine("Press Enter to stop recording, or type 'cancel' then Enter to discard.");

        var line = Console.ReadLine();
        SessionStatusResponse finalStatus;
        if (string.Equals(line?.Trim(), "cancel", StringComparison.OrdinalIgnoreCase))
        {
            finalStatus = await sessionClient.CancelAsync(created.SessionId);
        }
        else
        {
            finalStatus = await sessionClient.StopAsync(created.SessionId);
        }

        eventsCts.Cancel();
        try
        {
            await eventLoop;
        }
        catch (OperationCanceledException)
        {
        }

        Console.WriteLine(JsonSerializer.Serialize(finalStatus, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        }));

        return finalStatus.State is nameof(DictationSessionState.Completed) or nameof(DictationSessionState.Cancelled)
            ? 0
            : 1;
    }
    catch (ApiHostException ex)
    {
        Console.Error.WriteLine($"{ex.ErrorCode ?? "API_ERROR"}: {ex.Message}");
        return 1;
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
    Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    }));
    return 0;
}

static async Task<int> CancelAsync(string apiUrl, string? sessionId, string? apiToken)
{
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        Console.Error.WriteLine("Missing --session-id.");
        return 1;
    }

    await using var client = new ApiSessionClient(apiUrl, apiToken);
    var status = await client.CancelAsync(sessionId);
    Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    }));
    return 0;
}

static async Task<int> ApiAsync(string apiUrl)
{
    await using var client = new ApiSessionClient(apiUrl);
    var ready = await client.IsReadyAsync();
    Console.WriteLine($"ready={ready}");
    return ready ? 0 : 1;
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
        OrchestratorId = "alpha2-cli",
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

static async Task PrintEventsAsync(ApiSessionClient client, string sessionId, CancellationToken cancellationToken)
{
    await foreach (var evt in client.StreamEventsAsync(sessionId, cancellationToken))
    {
        var line = evt.EventType;
        if (!string.IsNullOrWhiteSpace(evt.State))
        {
            line += $": {evt.State}";
        }

        if (!string.IsNullOrWhiteSpace(evt.Text))
        {
            line += $" - {evt.Text}";
        }

        Console.WriteLine(line);
    }
}

static async Task<bool> EnsureApiReadyAsync(string apiUrl, int timeoutMs)
{
    await using var client = new ApiSessionClient(apiUrl);
    using var timeout = new CancellationTokenSource(timeoutMs);

    try
    {
        while (!timeout.IsCancellationRequested)
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
        var workingDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        var resolvedConfigPath = string.IsNullOrWhiteSpace(configPath)
            ? null
            : Path.GetFullPath(configPath);

        var arguments = new List<string>
        {
            "run",
            "--project",
            Quote(projectPath),
            "--",
            "--urls",
            Quote(apiUrl)
        };

        if (!string.IsNullOrWhiteSpace(resolvedConfigPath))
        {
            arguments.Add("--config");
            arguments.Add(Quote(resolvedConfigPath));
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = string.Join(' ', arguments),
                WorkingDirectory = workingDirectory,
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
    var target = Path.Combine("VoiceType2", "alpha-build-2", "src", "VoiceType2.Alpha2.ApiHost", "VoiceType2.Alpha2.ApiHost.csproj");
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
        }
    }

    throw new FileNotFoundException("Could not resolve VoiceType2.Alpha2.ApiHost project path.");
}

static int ParsePositiveInt(string? value, int defaultValue)
{
    return int.TryParse(value, out var parsed) && parsed > 0
        ? parsed
        : defaultValue;
}

static string Quote(string value) => $"\"{value}\"";

static int PrintUsage()
{
    Console.WriteLine("VoiceType2 Alpha 2 CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  vt2a2 run [--api-url <url>] [--mode attach|managed] [--api-config <path>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>]");
    Console.WriteLine("  vt2a2 status --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2a2 cancel --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2a2 api");
    return 1;
}

static ParsedArguments ParseArguments(string[] args)
{
    var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string? command = null;
    var showHelp = false;

    for (var i = 0; i < args.Length; i++)
    {
        var current = args[i];
        if (string.Equals(current, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current, "-h", StringComparison.OrdinalIgnoreCase))
        {
            showHelp = true;
        }
        else if (current.StartsWith("--", StringComparison.Ordinal))
        {
            var key = current;
            var value = "true";
            if (current.Contains('='))
            {
                var split = current.Split('=', 2, StringSplitOptions.TrimEntries);
                key = split[0];
                value = split[1];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            flags[key.ToLowerInvariant()] = value;
        }
        else if (command is null)
        {
            command = current;
        }
    }

    return new ParsedArguments((command ?? "run").ToLowerInvariant(), flags, showHelp);
}

internal sealed record ParsedArguments(string Command, Dictionary<string, string> Flags, bool ShowHelp)
{
    public string? GetFlagValue(string key) =>
        Flags.TryGetValue(key, out var value) ? value : null;
}
