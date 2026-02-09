using System.Runtime.InteropServices;

namespace VoiceType;

static class Program
{
    private enum LaunchRequest
    {
        Default,
        Close,
        Listen,
        ReplaceExisting
    }

    private const string MutexName = "VoiceType_SingleInstance";
    private const string ExitEventName = MutexName + "_Exit";
    private const string ListenEventName = MutexName + "_Listen";
    private static readonly TimeSpan ReplaceWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromSeconds(5);
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;
    private static EventWaitHandle? _exitEvent;
    private static EventWaitHandle? _listenEvent;

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
        if (requestPinToTaskbar || requestUnpinFromTaskbar)
        {
            EnsureConsoleForCliOutput();

            if (requestPinToTaskbar && requestUnpinFromTaskbar)
            {
                Console.Error.WriteLine("Specify only one taskbar command at a time.");
                Environment.ExitCode = 2;
                return;
            }

            var pin = requestPinToTaskbar;
            var succeeded = TaskbarPinManager.TrySetCurrentExecutablePinned(pin, out var message);
            if (succeeded)
                Console.WriteLine(message);
            else
                Console.Error.WriteLine(message);

            Environment.ExitCode = succeeded ? 0 : 1;
            return;
        }

        var requestClose = args.Contains("--close", StringComparer.OrdinalIgnoreCase);
        var requestListen = args.Contains("--listen", StringComparer.OrdinalIgnoreCase);
        var requestReplaceExisting = args.Contains("--replace-existing", StringComparer.OrdinalIgnoreCase);
        var launchRequest = GetLaunchRequest(requestClose, requestListen, requestReplaceExisting);
        if (launchRequest == null)
        {
            EnsureConsoleForCliOutput();
            Console.Error.WriteLine("Specify only one of: --close, --listen, --replace-existing.");
            Environment.ExitCode = 2;
            return;
        }

        // --test flag: dry-run to verify mic capture works (needs console)
        if (args.Contains("--test", StringComparer.OrdinalIgnoreCase))
        {
            EnsureConsoleForCliOutput();
            var testConfig = AppConfig.Load();
            Log.Configure(testConfig.EnableDebugLogging);
            RunTest().GetAwaiter().GetResult();
            return;
        }

        var request = launchRequest.Value;
        var listenAfterStartup = request == LaunchRequest.Listen;

        // Detach from any parent console so GUI launch returns immediately.
        FreeConsole();

        using var mutex = new Mutex(true, MutexName, out bool isNew);
        var ownsMutex = isNew;

        // If already running, route request to the existing process.
        if (!ownsMutex)
        {
            switch (request)
            {
                case LaunchRequest.Close:
                    if (SignalExistingInstanceExit())
                        _ = WaitForExistingInstanceExit(CloseWaitTimeout);
                    return;

                case LaunchRequest.Listen:
                    if (SignalExistingInstanceListen())
                        return;

                    // Fallback for older running versions that do not listen for remote listen signals.
                    if (SignalExistingInstanceExit())
                        _ = WaitForExistingInstanceExit(ReplaceWaitTimeout);
                    ownsMutex = TryTakeOverMutex(mutex);
                    listenAfterStartup = true;
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

        // `--close` targets an already-running instance and should not start a new UI.
        if (request == LaunchRequest.Close)
        {
            mutex.ReleaseMutex();
            return;
        }

        try
        {
            _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ExitEventName);
            _listenEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ListenEventName);

            var config = AppConfig.Load();
            Log.Configure(config.EnableDebugLogging);

            ApplicationConfiguration.Initialize();
            using var trayContext = new TrayContext();

            var exitThread = new Thread(() =>
            {
                try
                {
                    _exitEvent.WaitOne();
                    trayContext.RequestShutdown();
                }
                finally
                {
                    Environment.Exit(0);
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

            if (listenAfterStartup)
                trayContext.RequestListen();

            Application.Run(trayContext);
        }
        finally
        {
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
        bool requestListen,
        bool requestReplaceExisting)
    {
        var explicitRequestCount =
            (requestClose ? 1 : 0) +
            (requestListen ? 1 : 0) +
            (requestReplaceExisting ? 1 : 0);
        if (explicitRequestCount > 1)
            return null;

        if (requestClose)
            return LaunchRequest.Close;
        if (requestListen)
            return LaunchRequest.Listen;
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
        if (EventWaitHandle.TryOpenExisting(ListenEventName, out var evt))
        {
            evt.Set();
            evt.Dispose();
            return true;
        }

        return false;
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
        Console.WriteLine("  --close                   Signal running instance to close, then exit.");
        Console.WriteLine("  --replace-existing        Close running instance and start this one.");
        Console.WriteLine("  --pin-to-taskbar          Best-effort pin executable to taskbar.");
        Console.WriteLine("  --unpin-from-taskbar      Best-effort unpin executable from taskbar.");
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

    static async Task RunTest()
    {
        Console.WriteLine("=== VoiceType Dry-Run Test ===");
        Console.WriteLine();

        // 1. Test mic capture
        Console.WriteLine("[1/3] Testing microphone capture...");
        var recorder = new AudioRecorder();
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
            var config = AppConfig.Load();
            if (string.IsNullOrWhiteSpace(config.ApiKey))
            {
                Console.WriteLine("  WARNING - No API key configured. Run the app and go to Settings to add one.");
                Console.WriteLine($"  Config location: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceType", "config.json")}");
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
                    var svc = new TranscriptionService(config.ApiKey, config.Model);
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
        }
        finally
        {
            recorder.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("=== Test Complete ===");
    }
}
