using System.Runtime.InteropServices;

namespace VoiceType;

static class Program
{
    private enum LaunchRequest
    {
        Default,
        Activate,
        Close,
        Listen,
        Submit,
        ReplaceExisting
    }

    private const string MutexName = "VoiceType_SingleInstance";
    private const string ExitEventName = MutexName + "_Exit";
    private const string ListenEventName = MutexName + "_Listen";
    private const string ListenIgnorePrefixEventName = MutexName + "_ListenIgnorePrefix";
    private const string SubmitEventName = MutexName + "_Submit";
    private const string ActivateEventName = MutexName + "_Activate";
    private const string CloseCompletedEventNamePrefix = MutexName + "_CloseCompleted_";
    private static readonly TimeSpan ReplaceWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromMinutes(2);
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private static EventWaitHandle? _exitEvent;
    private static EventWaitHandle? _listenEvent;
    private static EventWaitHandle? _listenIgnorePrefixEvent;
    private static EventWaitHandle? _submitEvent;
    private static EventWaitHandle? _activateEvent;
    private static EventWaitHandle? _closeCompletedEvent;

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [STAThread]
    static void Main(string[] args)
    {
        AppInfo.Initialize();

        var requestHelp = args.Contains("--help", StringComparer.OrdinalIgnoreCase)
            || args.Contains("-h", StringComparer.OrdinalIgnoreCase);
        var requestVersion = args.Contains("--version", StringComparer.OrdinalIgnoreCase)
            || args.Contains("-v", StringComparer.OrdinalIgnoreCase);
        if (requestHelp || requestVersion)
        {
            EnsureConsoleForCliOutput();

            if (requestVersion)
                PrintVersion();

            if (requestHelp)
            {
                if (requestVersion)
                    Console.WriteLine();
                PrintHelp();
            }

            return;
        }

        var requestPinToTaskbar = args.Contains("--pin-to-taskbar", StringComparer.OrdinalIgnoreCase);
        var requestUnpinFromTaskbar = args.Contains("--unpin-from-taskbar", StringComparer.OrdinalIgnoreCase);
        var requestCreateActivateShortcut = args.Contains("--create-activate-shortcut", StringComparer.OrdinalIgnoreCase);
        var requestCreateSubmitShortcut = args.Contains("--create-submit-shortcut", StringComparer.OrdinalIgnoreCase);
        var requestCreateListenIgnorePrefixShortcut = args.Contains(
            "--create-listen-ignore-prefix-shortcut",
            StringComparer.OrdinalIgnoreCase);
        var utilityRequestCount =
            (requestPinToTaskbar ? 1 : 0) +
            (requestUnpinFromTaskbar ? 1 : 0) +
            (requestCreateActivateShortcut ? 1 : 0) +
            (requestCreateSubmitShortcut ? 1 : 0) +
            (requestCreateListenIgnorePrefixShortcut ? 1 : 0);
        if (utilityRequestCount > 1)
        {
            EnsureConsoleForCliOutput();
            Console.Error.WriteLine(
                "Specify only one of: --pin-to-taskbar, --unpin-from-taskbar, " +
                "--create-activate-shortcut, --create-submit-shortcut, " +
                "--create-listen-ignore-prefix-shortcut.");
            Environment.ExitCode = 2;
            return;
        }

        if (requestPinToTaskbar || requestUnpinFromTaskbar)
        {
            EnsureConsoleForCliOutput();

            var pin = requestPinToTaskbar;
            var succeeded = TaskbarPinManager.TrySetCurrentExecutablePinned(pin, out var message);
            if (succeeded)
                Console.WriteLine(message);
            else
                Console.Error.WriteLine(message);

            Environment.ExitCode = succeeded ? 0 : 1;
            return;
        }

        if (requestCreateActivateShortcut)
        {
            EnsureConsoleForCliOutput();
            var succeeded = ShortcutManager.TryCreateCurrentExecutableShortcut(
                shortcutFileName: "VoiceTypeActivate.exe.lnk",
                arguments: "--activate",
                description: "Trigger VoiceType activation flow",
                out var message);
            if (succeeded)
                Console.WriteLine(message);
            else
                Console.Error.WriteLine(message);

            Environment.ExitCode = succeeded ? 0 : 1;
            return;
        }

        if (requestCreateSubmitShortcut)
        {
            EnsureConsoleForCliOutput();
            var succeeded = ShortcutManager.TryCreateCurrentExecutableShortcut(
                shortcutFileName: "VoiceTypeSubmit.exe.lnk",
                arguments: "--submit",
                description: "Trigger VoiceType submit mode (or paste without auto-send during preview)",
                out var message);
            if (succeeded)
                Console.WriteLine(message);
            else
                Console.Error.WriteLine(message);

            Environment.ExitCode = succeeded ? 0 : 1;
            return;
        }

        if (requestCreateListenIgnorePrefixShortcut)
        {
            EnsureConsoleForCliOutput();
            var succeeded = ShortcutManager.TryCreateCurrentExecutableShortcut(
                shortcutFileName: "VoiceTypeListenNoPrefix.exe.lnk",
                arguments: "--listen --ignore-prefix",
                description: "Trigger VoiceType listen mode without pasted-text prefix",
                out var message);
            if (succeeded)
                Console.WriteLine(message);
            else
                Console.Error.WriteLine(message);

            Environment.ExitCode = succeeded ? 0 : 1;
            return;
        }

        var requestClose = args.Contains("--close", StringComparer.OrdinalIgnoreCase);
        var requestActivate = args.Contains("--activate", StringComparer.OrdinalIgnoreCase);
        var requestListen = args.Contains("--listen", StringComparer.OrdinalIgnoreCase);
        var requestIgnorePrefix = args.Contains("--ignore-prefix", StringComparer.OrdinalIgnoreCase);
        var requestSubmit = args.Contains("--submit", StringComparer.OrdinalIgnoreCase);
        var requestReplaceExisting = args.Contains("--replace-existing", StringComparer.OrdinalIgnoreCase);
        var launchRequest = GetLaunchRequest(
            requestClose,
            requestActivate,
            requestListen,
            requestSubmit,
            requestReplaceExisting);
        if (launchRequest == null)
        {
            EnsureConsoleForCliOutput();
            Console.Error.WriteLine("Specify only one of: --activate, --close, --listen, --submit, --replace-existing.");
            Environment.ExitCode = 2;
            return;
        }

        if (requestIgnorePrefix && !requestListen)
        {
            EnsureConsoleForCliOutput();
            Console.Error.WriteLine("--ignore-prefix can only be used with --listen.");
            Environment.ExitCode = 2;
            return;
        }

        // --test flag: dry-run to verify mic capture works (needs console)
        if (args.Contains("--test", StringComparer.OrdinalIgnoreCase))
        {
            EnsureConsoleForCliOutput();
            var testConfig = AppConfig.Load();
            if (testConfig.EnableDebugLogging)
                Log.RollOnStartup();
            Log.Configure(testConfig.EnableDebugLogging);
            Environment.ExitCode = RunTest().GetAwaiter().GetResult();
            return;
        }

        var request = launchRequest.Value;
        var listenAfterStartup = request == LaunchRequest.Listen;
        var activateAfterStartup = request == LaunchRequest.Activate;
        var listenIgnorePrefixOnStartup = listenAfterStartup && requestIgnorePrefix;
        var routedToExistingInstance = false;

        // Detach from any parent console so GUI launch returns immediately.
        FreeConsole();

        using var mutex = new Mutex(true, MutexName, out bool isNew);
        var ownsMutex = isNew;

        // If already running, route request to the existing process.
        if (!ownsMutex)
        {
            switch (request)
            {
                case LaunchRequest.Activate:
                    if (SignalExistingInstanceActivate())
                        return;
                    break;

                case LaunchRequest.Close:
                    routedToExistingInstance = SignalExistingInstanceExit();
                    if (routedToExistingInstance)
                    {
                        if (!WaitForExistingInstanceExit(CloseWaitTimeout))
                        {
                            ForceCloseExistingInstance();
                        }
                    }

                    break;

                case LaunchRequest.Listen:
                    if (SignalExistingInstanceListen(ignorePrefix: requestIgnorePrefix))
                        return;

                    // Fallback for older running versions that do not listen for remote listen signals.
                    if (SignalExistingInstanceExit())
                        _ = WaitForExistingInstanceExit(ReplaceWaitTimeout);
                    ownsMutex = TryTakeOverMutex(mutex);
                    listenAfterStartup = true;
                    listenIgnorePrefixOnStartup = requestIgnorePrefix;
                    break;

                case LaunchRequest.Submit:
                    routedToExistingInstance = SignalExistingInstanceSubmit();
                    break;

                case LaunchRequest.ReplaceExisting:
                    if (SignalExistingInstanceExit())
                        _ = WaitForExistingInstanceExit(ReplaceWaitTimeout);
                    ownsMutex = TryTakeOverMutex(mutex);
                    break;

                default:
                    // Normal relaunch prefers "listen existing", but fallback keeps compatibility.
                    if (SignalExistingInstanceListen())
                        return;

                    if (SignalExistingInstanceExit())
                        _ = WaitForExistingInstanceExit(ReplaceWaitTimeout);
                    ownsMutex = TryTakeOverMutex(mutex);
                    break;
            }
        }

        if (!ownsMutex)
            return;

        // `--close` and `--submit` target an already-running instance and should not start a new UI.
        if (request is LaunchRequest.Close or LaunchRequest.Submit)
        {
            if (!routedToExistingInstance)
            {
                EnsureConsoleForCliOutput();
                Console.Error.WriteLine(
                    request == LaunchRequest.Close
                        ? "No running VoiceType instance found to close."
                        : "No running VoiceType instance found for submit.");
                Environment.ExitCode = 1;
            }

            mutex.ReleaseMutex();
            return;
        }

        try
        {
            _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
            _listenEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ListenEventName);
            _listenIgnorePrefixEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                ListenIgnorePrefixEventName);
            _submitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, SubmitEventName);
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            _closeCompletedEvent = new EventWaitHandle(
                false,
                EventResetMode.ManualReset,
                $"{CloseCompletedEventNamePrefix}{Environment.ProcessId}");

            var config = AppConfig.Load();
            if (config.EnableDebugLogging)
                Log.RollOnStartup();
            Log.Configure(config.EnableDebugLogging);

            ApplicationConfiguration.Initialize();
            using var trayContext = new TrayContext(CreateOverlayManager());

            var exitThread = new Thread(() =>
            {
                try
                {
                    _exitEvent.WaitOne();
                    trayContext.RequestShutdown(fromRemoteAction: true);
                }
                catch (ObjectDisposedException)
                {
                    // App is already shutting down.
                }
            })
            { IsBackground = true };
            exitThread.Start();

            var listenThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _listenEvent.WaitOne();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    try
                    {
                        trayContext.RequestListen();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            })
            { IsBackground = true };
            listenThread.Start();

            var listenIgnorePrefixThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _listenIgnorePrefixEvent.WaitOne();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    try
                    {
                        trayContext.RequestListen(ignorePastedTextPrefix: true);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            })
            { IsBackground = true };
            listenIgnorePrefixThread.Start();

            var submitThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _submitEvent.WaitOne();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    try
                    {
                        trayContext.RequestSubmit();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            })
            { IsBackground = true };
            submitThread.Start();

            var activateThread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        _activateEvent.WaitOne();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    try
                    {
                        trayContext.RequestActivate();
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            })
            { IsBackground = true };
            activateThread.Start();

            if (listenAfterStartup)
                trayContext.RequestListen(ignorePastedTextPrefix: listenIgnorePrefixOnStartup);
            if (activateAfterStartup)
                trayContext.RequestActivate();

            Application.Run(trayContext);
        }
        finally
        {
            _closeCompletedEvent?.Set();
            _closeCompletedEvent?.Dispose();
            _closeCompletedEvent = null;
            _activateEvent?.Dispose();
            _activateEvent = null;
            _submitEvent?.Dispose();
            _submitEvent = null;
            _listenIgnorePrefixEvent?.Dispose();
            _listenIgnorePrefixEvent = null;
            _listenEvent?.Dispose();
            _listenEvent = null;
            _exitEvent?.Dispose();
            _exitEvent = null;
            mutex.ReleaseMutex();
        }
    }

    private static bool TryTakeOverMutex(Mutex mutex)
    {
        try
        {
            return mutex.WaitOne(ReplaceWaitTimeout);
        }
        catch (AbandonedMutexException)
        {
            // Previous instance died unexpectedly; we can continue.
            return true;
        }
    }

    private static LaunchRequest? GetLaunchRequest(
        bool requestClose,
        bool requestActivate,
        bool requestListen,
        bool requestSubmit,
        bool requestReplaceExisting)
    {
        var explicitRequestCount =
            (requestClose ? 1 : 0) +
            (requestActivate ? 1 : 0) +
            (requestListen ? 1 : 0) +
            (requestSubmit ? 1 : 0) +
            (requestReplaceExisting ? 1 : 0);
        if (explicitRequestCount > 1)
            return null;

        if (requestClose)
            return LaunchRequest.Close;
        if (requestActivate)
            return LaunchRequest.Activate;
        if (requestListen)
            return LaunchRequest.Listen;
        if (requestSubmit)
            return LaunchRequest.Submit;
        if (requestReplaceExisting)
            return LaunchRequest.ReplaceExisting;
        return LaunchRequest.Default;
    }

    private static bool SignalExistingInstanceExit()
    {
        if (EventWaitHandle.TryOpenExisting(ExitEventName, out var evt))
        {
            evt.Set();
            evt.Dispose();
            return true;
        }

        return false;
    }

    private static bool SignalExistingInstanceListen()
    {
        return SignalExistingInstanceListen(ignorePrefix: false);
    }

    private static bool SignalExistingInstanceListen(bool ignorePrefix)
    {
        if (EventWaitHandle.TryOpenExisting(
            ignorePrefix ? ListenIgnorePrefixEventName : ListenEventName,
            out var evt))
        {
            evt.Set();
            evt.Dispose();
            return true;
        }

        return false;
    }

    private static bool SignalExistingInstanceSubmit()
    {
        if (EventWaitHandle.TryOpenExisting(SubmitEventName, out var evt))
        {
            evt.Set();
            evt.Dispose();
            return true;
        }

        return false;
    }

    private static bool SignalExistingInstanceActivate()
    {
        if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var evt))
        {
            evt.Set();
            evt.Dispose();
            return true;
        }

        return false;
    }

    private static void ForceCloseExistingInstance()
    {
        var ownProcessId = Environment.ProcessId;
        string? ownProcessPath = null;
        try
        {
            ownProcessPath = Environment.ProcessPath;
        }
        catch
        {
            // Best effort only.
        }

        var processName = Environment.ProcessPath is null
            ? "VoiceType"
            : System.IO.Path.GetFileNameWithoutExtension(ownProcessPath)!;

        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                if (process.Id == ownProcessId)
                    continue;

                try
                {
                    if (ownProcessPath is not null)
                    {
                        string? candidatePath = null;
                        try
                        {
                            candidatePath = process.MainModule?.FileName;
                        }
                        catch
                        {
                            // Access to process metadata may fail for edge cases.
                        }

                        if (!string.IsNullOrWhiteSpace(candidatePath) &&
                            !string.Equals(
                                candidatePath,
                                ownProcessPath,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1000))
                            process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore and continue; this is best effort cleanup.
                }
                finally
                {
                    try
                    {
                        if (!process.HasExited)
                            process.WaitForExit(2000);
                    }
                    catch
                    {
                        // Ignore.
                    }

                    process.Dispose();
                }
            }
        }
        catch
        {
            // Ignore and continue if process enumeration fails.
        }
    }

    private static bool WaitForExistingInstanceExit(TimeSpan timeout)
    {
        using var probe = new Mutex(false, MutexName);
        try
        {
            if (!probe.WaitOne(timeout))
                return false;

            probe.ReleaseMutex();
            return true;
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    private static void PrintVersion()
    {
        Console.WriteLine($"VoiceType {AppInfo.Version}");
    }

    private static void PrintHelp()
    {
        Console.WriteLine("VoiceType");
        Console.WriteLine("Usage:");
        Console.WriteLine("  VoiceType.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --help, -h                Show this help text and exit.");
        Console.WriteLine("  --version, -v             Show app version and exit.");
        Console.WriteLine("  --test                    Run microphone/API dry-run test.");
        Console.WriteLine("  --listen                  Trigger dictation (existing instance or fresh start).");
        Console.WriteLine("  --ignore-prefix           Use with --listen to skip configured pasted text prefix for this invocation.");
        Console.WriteLine("  --submit                  Send Enter, or paste without auto-send if preview is active.");
        Console.WriteLine("  --close                   Request graceful close (finishes current work first).");
        Console.WriteLine("  --activate                Bring the existing instance to foreground and start listening.");
        Console.WriteLine("  --replace-existing        Close running instance and start this one.");
        Console.WriteLine("  --pin-to-taskbar          Best-effort pin executable to taskbar.");
        Console.WriteLine("  --unpin-from-taskbar      Best-effort unpin executable from taskbar.");
        Console.WriteLine("  --create-activate-shortcut  Create VoiceTypeActivate.exe.lnk for --activate.");
        Console.WriteLine("  --create-submit-shortcut  Create VoiceTypeSubmit.exe.lnk for --submit.");
        Console.WriteLine(
            "  --create-listen-ignore-prefix-shortcut " +
            "Create VoiceTypeListenNoPrefix.exe.lnk for --listen --ignore-prefix.");
        Console.WriteLine();
        Console.WriteLine("Default behavior:");
        Console.WriteLine("  Launching without options starts VoiceType, or triggers dictation in existing instance.");
    }

    private static void EnsureConsoleForCliOutput()
    {
        try
        {
            var hasOutputHandle = HasValidStdHandle(STD_OUTPUT_HANDLE) || HasValidStdHandle(STD_ERROR_HANDLE);
            if (!hasOutputHandle)
            {
                var hasConsole = AttachConsole(ATTACH_PARENT_PROCESS);
                if (!hasConsole)
                {
                    var errorCode = Marshal.GetLastWin32Error();
                    if (errorCode == 5) // ERROR_ACCESS_DENIED => already attached
                        hasConsole = true;
                    else
                        hasConsole = AllocConsole();
                }
            }
        }
        catch
        {
            // Best effort only
        }

        try
        {
            var standardOutput = Console.OpenStandardOutput();
            if (standardOutput != Stream.Null)
                Console.SetOut(new StreamWriter(standardOutput) { AutoFlush = true });

            var standardError = Console.OpenStandardError();
            if (standardError != Stream.Null)
                Console.SetError(new StreamWriter(standardError) { AutoFlush = true });
        }
        catch
        {
            // Best effort only
        }
    }

    private static bool HasValidStdHandle(int handleType)
    {
        var handle = GetStdHandle(handleType);
        return handle != IntPtr.Zero && handle != new IntPtr(-1);
    }

    static async Task<int> RunTest()
    {
        var exitCode = 0;

        Console.WriteLine("=== VoiceType Dry-Run Test ===");
        Console.WriteLine();

        // 1. Test mic capture
        Console.WriteLine("[1/3] Testing microphone capture...");
        var config = AppConfig.Load();
        var recorder = new AudioRecorder(config.MicrophoneInputDeviceIndex, config.MicrophoneInputDeviceName);
        try
        {
            recorder.Start();
            Console.WriteLine("  Recording 3 seconds... speak now!");
            await Task.Delay(3000);
            var audio = recorder.Stop();
            var metrics = recorder.LastCaptureMetrics;
            Console.WriteLine(
                $"  OK - Captured {audio.Length:N0} bytes ({metrics.Duration.TotalSeconds:F1}s, " +
                $"rms {metrics.Rms:F4}, peak {metrics.Peak:F4})");
            Console.WriteLine();

            // 2. Test API key config
            Console.WriteLine("[2/3] Checking API key configuration...");
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Console.WriteLine("  WARNING - No API key configured. Run the app and go to Settings to add one.");
                Console.WriteLine(
                    $"  Config location: {Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceType", "config.json")}");
                Console.WriteLine();
                Console.WriteLine("[3/3] Skipping transcription test (no API key).");
            }
            else
            {
                Console.WriteLine($"  OK - API key found (model: {config.Model})");
                Console.WriteLine();

                if (metrics.IsLikelySilence)
                {
                    Console.WriteLine("[3/3] Skipping transcription test (captured audio appears to be silence/noise).");
                }
                else
                {
                    // 3. Test transcription
                    Console.WriteLine("[3/3] Sending audio to OpenAI for transcription...");
                    var svc = new TranscriptionService(
                        config.ApiKey,
                        config.Model,
                        config.EnableTranscriptionPrompt,
                        config.TranscriptionPrompt);
            var text = await svc.TranscribeAsync(audio);
                    if (string.IsNullOrWhiteSpace(text))
                        Console.WriteLine("  WARNING - Transcription returned empty text. Did you speak?");
                    else
                        Console.WriteLine($"  OK - Transcribed: \"{text}\"");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAILED - {ex.Message}");
            exitCode = 1;
        }
        finally
        {
            recorder.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("=== Test Complete ===");

        return exitCode;
    }

    private static IOverlayManager CreateOverlayManager()
    {
        return new OverlayWindowManager();
    }
}
