using System.Runtime.InteropServices;

namespace VoiceType;

static class Program
{
    private const string MutexName = "VoiceType_SingleInstance";
    private static EventWaitHandle? _exitEvent;

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    [STAThread]
    static void Main(string[] args)
    {
        // --test flag: dry-run to verify mic capture works (needs console)
        if (args.Contains("--test", StringComparer.OrdinalIgnoreCase))
        {
            RunTest().GetAwaiter().GetResult();
            return;
        }

        // Detach from any parent console so the terminal returns immediately
        FreeConsole();

        // If already running, close the existing instance and exit
        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            if (EventWaitHandle.TryOpenExisting(MutexName + "_Exit", out var evt))
            {
                evt.Set();
                evt.Dispose();
            }
            return;
        }

        // Create a named event so other instances can signal us to exit
        _exitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, MutexName + "_Exit");

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

        _exitEvent.Dispose();
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
            Console.WriteLine($"  OK - Captured {audio.Length:N0} bytes ({audio.Length / 32000.0:F1}s of audio)");
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
