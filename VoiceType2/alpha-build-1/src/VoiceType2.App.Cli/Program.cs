using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Spectre.Console;
using VoiceType2.App.Cli;
using VoiceType2.Core.Contracts;


var input = ParseArguments(args);
var command = input.Command;
var defaults = ClientConfigLoader.Load(input.GetFlagValue("--client-config"));
var apiUrl = input.GetFlagValue("--api-url") ?? defaults.ApiUrl;
var apiToken = input.GetFlagValue("--api-token");
var sessionId = input.GetFlagValue("--session-id");
var mode = input.GetFlagValue("--mode") ?? defaults.Mode;
var managedApiConfig = input.GetFlagValue("--api-config");
var apiTimeoutMs = ParseInt(input.GetFlagValue("--api-timeout-ms"), defaults.ApiTimeoutMs);
var shutdownTimeoutMs = ParseInt(input.GetFlagValue("--shutdown-timeout-ms"), defaults.ShutdownTimeoutMs);
var managedStart = ParseBool(input.GetFlagValue("--managed-start"), defaults.ManagedStart);
var sessionMode = input.GetFlagValue("--session-mode") ?? defaults.SessionMode;
var recordingDeviceId = input.GetFlagValue("--recording-device-id") ?? defaults.DefaultRecordingDeviceId;
var playbackDeviceId = input.GetFlagValue("--playback-device-id") ?? defaults.DefaultPlaybackDeviceId;

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
    "run" => await RunAsync(
        apiUrl,
        mode,
        managedStart,
        managedApiConfig,
        apiTimeoutMs,
        shutdownTimeoutMs,
        recordingDeviceId,
        playbackDeviceId),
    "tui" => await TuiAsync(
        apiUrl,
        mode,
        managedStart,
        managedApiConfig,
        apiTimeoutMs,
        shutdownTimeoutMs,
        recordingDeviceId,
        playbackDeviceId),
    "status" => await StatusAsync(apiUrl, sessionId, apiToken),
    "stop" => await StopAsync(apiUrl, sessionId, apiToken),
    "resolve" => await ResolveAsync(apiUrl, sessionId, input.PositionalArgs, apiToken),
    "api" => await ApiAsync(apiUrl, input.PositionalArgs),
        _ => PrintUsage(),
};

Environment.ExitCode = exitCode;

return;

async Task<int> RunAsync(
    string apiUrl,
    string mode,
    bool managedStart,
    string? managedApiConfig,
    int apiTimeoutMs,
    int shutdownTimeoutMs,
    string? recordingDeviceId,
    string? playbackDeviceId)
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
        var selectedDevices = new AudioDeviceSelectionState(
            string.IsNullOrWhiteSpace(recordingDeviceId) ? null : recordingDeviceId,
            string.IsNullOrWhiteSpace(playbackDeviceId) ? null : playbackDeviceId);

        var profile = CreateProfile();
        var created = await bootstrapClient.RegisterAsync(
            profile,
            sessionMode,
            CreateAudioDeviceSelection(selectedDevices));
        await using var sessionClient = new ApiSessionClient(apiUrl, created.OrchestratorToken);

        await sessionClient.StartAsync(created.SessionId);
        PrintSessionStartedHeader(
            created.SessionId,
            created.State,
            created.CorrelationId,
            apiUrl,
            mode,
            selectedDevices);
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

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                continue;
            }

            var command = tokens[0].Trim().ToLowerInvariant();
            var argument = tokens.Length > 1 ? string.Join(" ", tokens.Skip(1)) : null;

            if (command is "status" or "devices" or "recording-device" or "playback-device" or "set-recording-device" or "set-playback-device")
            {
                if (await HandleRunDeviceCommandsAsync(
                        command,
                        argument,
                        selectedDevices,
                        created.SessionId,
                        sessionClient))
                {
                    PrintRunMenu();
                    continue;
                }
            }

            if (command is "q" or "quit" or "exit")
            {
                Console.WriteLine("Stopping session and exiting...");
                break;
            }

            if (command is "s" or "submit")
            {
                await sessionClient.ResolveAsync(created.SessionId, "submit");
                Console.WriteLine("Action sent: submit");
                continue;
            }

            if (command is "c" or "cancel")
            {
                await sessionClient.ResolveAsync(created.SessionId, "cancel");
                Console.WriteLine("Action sent: cancel");
                continue;
            }

            if (command is "r" or "retry")
            {
                await sessionClient.ResolveAsync(created.SessionId, "retry");
                Console.WriteLine("Action sent: retry");
                continue;
            }

            if (command is "status")
            {
                await PrintSessionStatusAsync(sessionClient, created.SessionId);
                continue;
            }

            if (command is "h" or "help" or "menu")
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

async Task<int> TuiAsync(
    string apiUrl,
    string mode,
    bool managedStart,
    string? managedApiConfig,
    int apiTimeoutMs,
    int shutdownTimeoutMs,
    string? recordingDeviceId,
    string? playbackDeviceId)
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
        var selectedDevices = new AudioDeviceSelectionState(
            string.IsNullOrWhiteSpace(recordingDeviceId) ? null : recordingDeviceId,
            string.IsNullOrWhiteSpace(playbackDeviceId) ? null : playbackDeviceId);
        var profile = CreateProfile();
        var created = await bootstrapClient.RegisterAsync(
            profile,
            sessionMode,
            CreateAudioDeviceSelection(selectedDevices));
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
                    .AddChoices(
                        "submit",
                        "cancel",
                        "retry",
                        "set recording device",
                        "set playback device",
                        "show devices",
                        "status",
                        "help",
                        "quit"));

            if (selected is "quit")
            {
                break;
            }

            if (selected is "help")
            {
                PrintTuiMenu();
                continue;
            }

            if (selected is "show devices")
            {
                await PrintAvailableDevicesAsync(selectedDevices, sessionClient);
                continue;
            }

            if (selected is "set recording device")
            {
                await SelectRecordingDeviceAsync(selectedDevices, created.SessionId, sessionClient);
                continue;
            }

            if (selected is "set playback device")
            {
                await SelectPlaybackDeviceAsync(selectedDevices, created.SessionId, sessionClient);
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

async Task<int> StatusAsync(string apiUrl, string? sessionId, string? apiToken)
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

async Task<int> StopAsync(string apiUrl, string? sessionId, string? apiToken)
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

async Task<int> ResolveAsync(string apiUrl, string? sessionId, string[] positionalArgs, string? apiToken)
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

async Task<int> ApiAsync(string apiUrl, string[] positionalArgs)
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

async Task PrintEventsAsync(ApiSessionClient client, string sessionId, CancellationToken ct)
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

async Task PrintTuiEventsAsync(ApiSessionClient client, string sessionId, CancellationToken ct)
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

void PrintSessionStartedHeader(
    string sessionId,
    string state,
    string correlationId,
    string apiUrl,
    string mode,
    AudioDeviceSelectionState selectedDevices)
{
    var playbackSelection = string.IsNullOrWhiteSpace(selectedDevices.PlaybackDeviceId)
        ? "<auto>"
        : selectedDevices.PlaybackDeviceId;
    var recordingSelection = string.IsNullOrWhiteSpace(selectedDevices.RecordingDeviceId)
        ? "<auto>"
        : selectedDevices.RecordingDeviceId;

    Console.WriteLine();
    Console.WriteLine("=== VoiceType2 Dictation Session ===");
    Console.WriteLine($"Mode:        {mode}");
    Console.WriteLine($"API URL:     {apiUrl}");
    Console.WriteLine($"Session ID:  {sessionId}");
    Console.WriteLine($"State:       {state}");
    Console.WriteLine($"Correlation: {correlationId}");
    Console.WriteLine($"Recording:   {recordingSelection}");
    Console.WriteLine($"Playback:    {playbackSelection}");
    Console.WriteLine("=================================");
    Console.WriteLine();
}

void PrintRunMenu()
{
    PrintRunMenuForSelection(new AudioDeviceSelectionState(null, null));
}

void PrintRunMenuForSelection(AudioDeviceSelectionState selectedDevices)
{
    var playbackSelection = string.IsNullOrWhiteSpace(selectedDevices.PlaybackDeviceId)
        ? "<auto>"
        : selectedDevices.PlaybackDeviceId;
    var recordingSelection = string.IsNullOrWhiteSpace(selectedDevices.RecordingDeviceId)
        ? "<auto>"
        : selectedDevices.RecordingDeviceId;

    Console.WriteLine("Session menu (enter a command and press Enter):");
    Console.WriteLine("  1) submit  (s)  - Accept transcript and complete");
    Console.WriteLine("  2) cancel  (c)  - Cancel current transcript");
    Console.WriteLine("  3) retry   (r)  - Retry transcription");
    Console.WriteLine($"  4) recording-device <id|index> ({recordingSelection}) - Select recording device");
    Console.WriteLine($"  5) playback-device  <id|index> ({playbackSelection}) - Select playback device");
    Console.WriteLine("  6) list-devices               - List available devices");
    Console.WriteLine("  7) status                    - Show current status");
    Console.WriteLine("  8) quit    (q)               - Stop session and exit");
    Console.WriteLine("  9) help    (h)               - Show this menu again");
    Console.WriteLine("=================================");
}

void PrintTuiMenu()
{
    PrintTuiMenuForSelection(new AudioDeviceSelectionState(null, null));
}

void PrintTuiMenuForSelection(AudioDeviceSelectionState selectedDevices)
{
    var playbackSelection = string.IsNullOrWhiteSpace(selectedDevices.PlaybackDeviceId)
        ? "<auto>"
        : selectedDevices.PlaybackDeviceId;
    var recordingSelection = string.IsNullOrWhiteSpace(selectedDevices.RecordingDeviceId)
        ? "<auto>"
        : selectedDevices.RecordingDeviceId;

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
        .AddRow("[green]set recording device[/]", $"Set recording device ({recordingSelection})")
        .AddRow("[green]set playback device[/]", $"Set playback device ({playbackSelection})")
        .AddRow("[green]show devices[/]", "Show available recording and playback devices")
        .AddRow("[green]status[/]", "Show current status")
        .AddRow("[green]help[/]", "Show this menu")
        .AddRow("[green]quit[/]", "Stop session and exit"));
}

async Task PrintSessionStatusAsync(ApiSessionClient client, string sessionId)
{
    var status = await client.GetStatusAsync(sessionId);
    Console.WriteLine(
        $"sessionId={status.SessionId} state={status.State} " +
        $"lastEvent={status.LastEvent} correlationId={status.CorrelationId} revision={status.Revision}");
}

void PrintSessionEvent(SessionEventEnvelope evt)
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

void PrintSessionEventForTui(SessionEventEnvelope evt)
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

async Task<bool> HandleRunDeviceCommandsAsync(
    string command,
    string? argument,
    AudioDeviceSelectionState selections,
    string sessionId,
    ApiSessionClient sessionClient)
{
    if (command is "devices" or "list-devices")
    {
        await PrintAvailableDevicesAsync(selections, sessionClient);
        return true;
    }

    if (command is "status")
    {
        await PrintSessionStatusAsync(sessionClient, sessionId);
        return true;
    }

    if (await TrySetDeviceFromCommandAsync(command, argument, selections, sessionClient, sessionId))
    {
        return true;
    }

    return false;
}

async Task<bool> TrySetDeviceFromCommandAsync(
    string command,
    string? argument,
    AudioDeviceSelectionState selections,
    ApiSessionClient sessionClient,
    string sessionId)
{
    var previousRecordingId = selections.RecordingDeviceId;
    var previousPlaybackId = selections.PlaybackDeviceId;

    if (string.IsNullOrWhiteSpace(argument))
    {
        Console.WriteLine($"Missing argument for '{command}'.");
        return false;
    }

    var commandIsRecording = command is "recording-device" or "set-recording-device";
    var commandIsPlayback = command is "playback-device" or "set-playback-device";

    if (!commandIsRecording && !commandIsPlayback)
    {
        return false;
    }

    var devices = commandIsRecording
        ? await GetRecordingDevicesAsync(sessionClient)
        : await GetPlaybackDevicesAsync(sessionClient);

    if (devices.Count == 0)
    {
        Console.WriteLine("No audio devices found for this category.");
        return false;
    }

    if (TryResolveDeviceSelection(devices, argument, out var selected))
    {
        if (commandIsRecording)
        {
            selections.RecordingDeviceId = selected;
            Console.WriteLine($"Recording device set to: {DescribeDevice(devices, selected)}");
        }
        else
        {
            selections.PlaybackDeviceId = selected;
            Console.WriteLine($"Playback device set to: {DescribeDevice(devices, selected)}");
        }

        if (await SyncSessionAudioSelectionAsync(sessionClient, sessionId, selections))
        {
            return true;
        }

        selections.RecordingDeviceId = previousRecordingId;
        selections.PlaybackDeviceId = previousPlaybackId;
        Console.WriteLine("Unable to persist audio device selection to API session.");
        return false;
    }

    Console.WriteLine("Unable to match a device by that index or id.");
    return false;
}

async Task<bool> SyncSessionAudioSelectionAsync(
    ApiSessionClient sessionClient,
    string sessionId,
    AudioDeviceSelectionState selections)
{
    try
    {
        await sessionClient.UpdateDevicesAsync(
            sessionId,
            new AudioDeviceSelection
            {
                RecordingDeviceId = selections.RecordingDeviceId,
                PlaybackDeviceId = selections.PlaybackDeviceId
            });
        return true;
    }
    catch (ApiHostException ex)
    {
        Console.WriteLine($"Unable to persist audio device selection: {ex.Message}");
        return false;
    }
}

void PrintAvailableDeviceHeader(string heading, string currentSelection)
{
    Console.WriteLine();
    Console.WriteLine(heading);
    Console.WriteLine($"Current: {(string.IsNullOrWhiteSpace(currentSelection) ? "<auto>" : currentSelection)}");
}

void PrintDeviceList(string header, IReadOnlyList<HostAudioDevice> devices, string currentSelection)
{
    if (devices.Count == 0)
    {
        PrintAvailableDeviceHeader(header, currentSelection);
        Console.WriteLine("  (none discovered)");
        return;
    }

    PrintAvailableDeviceHeader(header, currentSelection);
    for (var index = 0; index < devices.Count; index++)
    {
        var device = devices[index];
        var marker = string.Equals(device.DeviceId, currentSelection, StringComparison.Ordinal)
            ? " [selected]"
            : string.Empty;
        Console.WriteLine($"  {index + 1,2}. {device.Name} ({device.DeviceId}){marker}");
    }

    Console.WriteLine();
}

async Task PrintAvailableDevicesAsync(AudioDeviceSelectionState selections, ApiSessionClient sessionClient)
{
    var recorders = await GetRecordingDevicesAsync(sessionClient);
    var players = await GetPlaybackDevicesAsync(sessionClient);

    PrintDeviceList("Recording devices:", recorders, selections.RecordingDeviceId ?? string.Empty);
    PrintDeviceList("Playback devices:", players, selections.PlaybackDeviceId ?? string.Empty);
}

async Task SelectRecordingDeviceAsync(
    AudioDeviceSelectionState selections,
    string sessionId,
    ApiSessionClient sessionClient)
{
    var devices = await GetRecordingDevicesAsync(sessionClient);
    if (devices.Count == 0)
    {
        Console.WriteLine("No recording devices discovered.");
        return;
    }

    var choices = new List<string> { "(auto)" };
    choices.AddRange(devices.Select(d => $"{d.DeviceId}|{d.Name}"));
    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select recording device")
            .AddChoices(choices));

    if (selected is "(auto)")
    {
        selections.RecordingDeviceId = null;
        await SyncSessionAudioSelectionAsync(sessionClient, sessionId, selections);
        return;
    }

    selections.RecordingDeviceId = selected.Split('|')[0];
    await SyncSessionAudioSelectionAsync(sessionClient, sessionId, selections);
}

async Task SelectPlaybackDeviceAsync(
    AudioDeviceSelectionState selections,
    string sessionId,
    ApiSessionClient sessionClient)
{
    var devices = await GetPlaybackDevicesAsync(sessionClient);
    if (devices.Count == 0)
    {
        Console.WriteLine("No playback devices discovered.");
        return;
    }

    var choices = new List<string> { "(auto)" };
    choices.AddRange(devices.Select(d => $"{d.DeviceId}|{d.Name}"));
    var selected = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Select playback device")
            .AddChoices(choices));

    if (selected is "(auto)")
    {
        selections.PlaybackDeviceId = null;
        await SyncSessionAudioSelectionAsync(sessionClient, sessionId, selections);
        return;
    }

    selections.PlaybackDeviceId = selected.Split('|')[0];
    await SyncSessionAudioSelectionAsync(sessionClient, sessionId, selections);
}

AudioDeviceSelection? CreateAudioDeviceSelection(AudioDeviceSelectionState selections)
{
    if (string.IsNullOrWhiteSpace(selections.RecordingDeviceId) && string.IsNullOrWhiteSpace(selections.PlaybackDeviceId))
    {
        return null;
    }

    return new AudioDeviceSelection
    {
        RecordingDeviceId = selections.RecordingDeviceId,
        PlaybackDeviceId = selections.PlaybackDeviceId
    };
}

string DescribeDevice(IReadOnlyList<HostAudioDevice> devices, string selectedDeviceId)
{
    foreach (var device in devices)
    {
        if (string.Equals(device.DeviceId, selectedDeviceId, StringComparison.Ordinal))
        {
            return device.Name;
        }
    }

    return selectedDeviceId;
}

bool TryResolveDeviceSelection(IReadOnlyList<HostAudioDevice> devices, string input, out string selectedId)
{
    selectedId = input.Trim();

    if (int.TryParse(selectedId, out var index) && index > 0 && index <= devices.Count)
    {
        selectedId = devices[index - 1].DeviceId;
        return true;
    }

    foreach (var device in devices)
    {
        if (string.Equals(device.DeviceId, selectedId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    selectedId = string.Empty;
    return false;
}

async Task<IReadOnlyList<HostAudioDevice>> GetRecordingDevicesAsync(ApiSessionClient sessionClient, CancellationToken ct = default)
{
    var discovered = await GetHostDiscoveredDevicesAsync(sessionClient, ct);
    return discovered.RecordingDevices.Count > 0
        ? discovered.RecordingDevices
        : GetLocalRecordingDevices();
}

async Task<IReadOnlyList<HostAudioDevice>> GetPlaybackDevicesAsync(ApiSessionClient sessionClient, CancellationToken ct = default)
{
    var discovered = await GetHostDiscoveredDevicesAsync(sessionClient, ct);
    return discovered.PlaybackDevices.Count > 0
        ? discovered.PlaybackDevices
        : GetLocalPlaybackDevices();
}

async Task<(IReadOnlyList<HostAudioDevice> RecordingDevices, IReadOnlyList<HostAudioDevice> PlaybackDevices)> GetHostDiscoveredDevicesAsync(
    ApiSessionClient sessionClient,
    CancellationToken ct = default)
{
    try
    {
        var hostDevices = await sessionClient.GetDevicesAsync(ct);
        return (hostDevices.RecordingDevices, hostDevices.PlaybackDevices);
    }
    catch
    {
        return (Array.Empty<HostAudioDevice>(), Array.Empty<HostAudioDevice>());
    }
}

IReadOnlyList<HostAudioDevice> GetLocalRecordingDevices()
{
    var devices = new List<HostAudioDevice>();

    if (OperatingSystem.IsWindows())
    {
        return GetWindowsRecordingDevices();
    }

    if (OperatingSystem.IsLinux())
    {
        return GetLinuxDevices("arecord -l", "rec");
    }

    if (OperatingSystem.IsMacOS())
    {
        return GetMacDevices(true);
    }

    return devices;
}

IReadOnlyList<HostAudioDevice> GetLocalPlaybackDevices()
{
    var devices = new List<HostAudioDevice>();

    if (OperatingSystem.IsWindows())
    {
        return GetWindowsPlaybackDevices();
    }

    if (OperatingSystem.IsLinux())
    {
        return GetLinuxDevices("aplay -l", "play");
    }

    if (OperatingSystem.IsMacOS())
    {
        return GetMacDevices(false);
    }

    return devices;
}

IReadOnlyList<HostAudioDevice> GetWindowsRecordingDevices()
{
    return GetWindowsWaveDevices("NAudio.Wave.WaveIn", "rec");
}

IReadOnlyList<HostAudioDevice> GetWindowsPlaybackDevices()
{
    return GetWindowsWaveDevices("NAudio.Wave.WaveOut", "play");
}

IReadOnlyList<HostAudioDevice> GetWindowsWaveDevices(string typeName, string prefix)
{
    try
    {
        var type = Type.GetType($"{typeName}, NAudio");
        if (type is null)
        {
            return new List<HostAudioDevice>();
        }

        var deviceCountProperty = type.GetProperty(
            "DeviceCount",
            BindingFlags.Public | BindingFlags.Static);
        if (deviceCountProperty is null)
        {
            return new List<HostAudioDevice>();
        }

        var getCapabilitiesMethod = type.GetMethod(
            "GetCapabilities",
            BindingFlags.Public | BindingFlags.Static);
        if (getCapabilitiesMethod is null)
        {
            return new List<HostAudioDevice>();
        }

        var count = (int)(deviceCountProperty.GetValue(null) ?? 0);
        var devices = new List<HostAudioDevice>();
        for (var index = 0; index < count; index++)
        {
            var capabilities = getCapabilitiesMethod.Invoke(null, new object[] { index });
            if (capabilities is null)
            {
                continue;
            }

            var name = capabilities.GetType()
                .GetProperty("ProductName")
                ?.GetValue(capabilities) as string;

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

    return new List<HostAudioDevice>();
}

IReadOnlyList<HostAudioDevice> GetLinuxDevices(string command, string prefix)
{
    var output = RunCommand(command);
    if (string.IsNullOrWhiteSpace(output))
    {
        return new List<HostAudioDevice>();
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

IReadOnlyList<HostAudioDevice> GetMacDevices(bool isRecording)
{
    var output = RunCommand("system_profiler SPAudioDataType");
    if (string.IsNullOrWhiteSpace(output))
    {
        return new List<HostAudioDevice>();
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

        if (!section.Equals(sectionId, StringComparison.Ordinal))
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

string RunCommand(string commandLine)
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
        process.WaitForExit(1000);

        return output.ToString();
    }
    catch
    {
        return string.Empty;
    }
}

OrchestratorProfile CreateProfile()
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

bool TryNormalizeAction(string action, out string normalized)
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

async Task<bool> EnsureApiReadyAsync(string apiUrl, int timeoutMs)
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

Process? StartManagedApi(string apiUrl, string? configPath)
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

void StopManagedApi(Process process, int timeoutMs)
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

string FindApiHostProjectPath()
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

string Quote(string value)
{
    return $"\"{value}\"";
}

bool ParseBool(string? value, bool defaultValue)
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

int ParseInt(string? value, int defaultValue)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultValue;
    }

    return int.TryParse(value, out var parsed) && parsed > 0
        ? parsed
        : defaultValue;
}

int PrintUsage()
{
    Console.WriteLine("VoiceType2 CLI (Alpha 1)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  vt2 run [--api-url <url>] [--mode attach|managed] [--session-mode <mode>] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--recording-device-id <id>] [--playback-device-id <id>] [--api-config <path>] [--client-config <path>]");
    Console.WriteLine("  vt2 tui [--api-url <url>] [--mode attach|managed] [--session-mode <mode>] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--recording-device-id <id>] [--playback-device-id <id>] [--api-config <path>] [--client-config <path>]");
    Console.WriteLine("  vt2 --tui [--mode attach|managed] [--api-url <url>] [--session-mode <mode>] [--api-token <token>] [--api-timeout-ms <ms>] [--shutdown-timeout-ms <ms>] [--managed-start true|false] [--recording-device-id <id>] [--playback-device-id <id>] [--api-config <path>] [--client-config <path>]");
    Console.WriteLine("  vt2 status --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 stop --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 resolve <submit|cancel|retry> --session-id <id> [--api-url <url>] [--api-token <token>]");
    Console.WriteLine("  vt2 api [status]");
    return 1;
}

ParsedArguments ParseArguments(string[] args)
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
