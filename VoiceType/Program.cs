using System.Runtime.InteropServices;

namespace VoiceType;

static class Program
{
    private const string MutexName = "VoiceType_SingleInstance";
    private static readonly TimeSpan ReplaceWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromSeconds(5);
    private static EventWaitHandle? _exitEvent;

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [STAThread]
    static void Main(string[] args)
    {
        var requestReplace = args.Contains("--replace-existing", StringComparer.OrdinalIgnoreCase);
        var requestClose = args.Contains("--close", StringComparer.OrdinalIgnoreCase);

        // --test flag: dry-run to verify mic capture works (needs console)
        if (args.Contains("--test", StringComparer.OrdinalIgnoreCase))
        {
            var testConfig = AppConfig.Load();
            Log.Configure(testConfig.EnableDebugLogging);
            RunTest().GetAwaiter().GetResult();
            return;
        }

        // Detach from any parent console so the terminal returns immediately
        FreeConsole();

        using var mutex = new Mutex(true, MutexName, out bool isNew);
        var ownsMutex = isNew;

        // If already running, explicit modes can signal it to exit.
        if (!ownsMutex)
        {
            if (requestClose || requestReplace)
            {
                var signaled = SignalExistingInstanceExit();
                if (signaled)
                    _ = WaitForExistingInstanceExit(requestReplace ? ReplaceWaitTimeout : CloseWaitTimeout);
            }

            if (requestReplace)
                ownsMutex = TryTakeOverMutex(mutex);
        }

        if (!ownsMutex)
            return;

        // `--close` is intended to close an already-running instance, not start a new one.
        if (requestClose)
        {
            mutex.ReleaseMutex();
            return;
        }

        try
        {
            // Create a named event so other instances can signal us to exit
            _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, MutexName + "_Exit");

            var config = AppConfig.Load();
            Log.Configure(config.EnableDebugLogging);

            // Watch for the exit signal on a background thread
            var exitThread = new Thread(() =>
            {
                _exitEvent.WaitOne();
                Application.Exit();
            })
            { IsBackground = true };
            exitThread.Start();

            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext());
        }
        finally
        {
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

    private static bool SignalExistingInstanceExit()
    {
        if (EventWaitHandle.TryOpenExisting(MutexName + "_Exit", out var evt))
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
