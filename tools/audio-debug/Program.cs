using System.Globalization;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceType;

namespace VoiceTypeAudioDebug;

internal static class Program
{
    private const int DefaultCaptureDurationMs = 3000;
    private const int DefaultDingDurationMs = 900;
    private const int MinCaptureDurationMs = 250;
    private const int MaxCaptureDurationMs = 300000;

    private static async Task<int> Main(string[] args)
    {
            var options = ParseOptions(args);
        if (options.Help)
        {
            PrintHelp();
            return 0;
        }

        try
        {
            if (options.FromConfig)
            {
                ApplyConfigDefaults(ref options);
            }

            PrintDeviceTable();
            PrintDeviceSelectionWarnings(options);

            if (options.ListOnly)
                return 0;

            var result = options.Ding
                ? await RunDingTestAsync(options)
                : await RunMicValidationAsync(options);
            return result ? 0 : 1;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            PrintHelp();
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] {ex.Message}");
            return 1;
        }
    }

    private static async Task<bool> RunDingTestAsync(DebugOptions options)
    {
        var requestedOutputSummary = options.OutputIndex < 0
            ? "system default output"
            : $"index {options.OutputIndex}";

        Console.WriteLine("=== VoiceType output test ===");
        Console.WriteLine($"Output device request: {requestedOutputSummary}");
        Console.WriteLine($"Ding duration: {DefaultDingDurationMs} ms");
        Console.WriteLine();

        try
        {
            Console.WriteLine("[1/1] Playing ding...");
            await PlayDingToneAsync(options.OutputIndex, options.OutputVolume, cancellationToken: default);
            Console.WriteLine("[1/1] Ding playback succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[1/1] Ding playback failed.");
            Console.WriteLine($"  {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RunMicValidationAsync(DebugOptions options)
    {
        var inputIndex = options.InputIndex;
        var inputName = options.InputName;
        var outputIndex = options.OutputIndex;
        var durationMs = options.DurationMs;
        var outputVolume = options.OutputVolume;
        var requestedInputSummary = string.IsNullOrWhiteSpace(inputName)
            ? $"index {inputIndex}"
            : $"'{inputName}' (index {inputIndex})";
        var requestedOutputSummary = outputIndex < 0
            ? "system default output"
            : $"index {outputIndex}";

        Console.WriteLine("=== VoiceType audio validation ===");
        Console.WriteLine($"Input device request: {requestedInputSummary}");
        Console.WriteLine($"Output device request: {requestedOutputSummary}");
        Console.WriteLine($"Capture duration: {durationMs} ms");
        Console.WriteLine();
        using var recorder = new AudioRecorder(inputIndex, inputName);

        try
        {
            recorder.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[1/2] Capture failed to start.");
            Console.WriteLine($"  {ex.Message}");
            return false;
        }

        Console.WriteLine("[1/2] Capturing...");
        await Task.Delay(durationMs);
        byte[] audio;
        AudioCaptureMetrics metrics;
        AudioCaptureSelectionState captureSelection;
        try
        {
            audio = recorder.Stop();
            metrics = recorder.LastCaptureMetrics;
            captureSelection = recorder.LastCaptureSelection;
            var selectionSummary = $"requested={captureSelection.RequestedSummary}, " +
                $"active={captureSelection.ActiveSummary}, reason={captureSelection.SelectionReason}, " +
                $"fallback={captureSelection.UsedFallback}";

            Console.WriteLine("      Selection: " + selectionSummary);
            if (captureSelection.Attempts.Count > 0)
            {
                foreach (var attempt in captureSelection.Attempts)
                {
                    Console.WriteLine($"      Attempt: {attempt}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[1/2] Capture failed to stop.");
            Console.WriteLine($"  {ex.Message}");
            return false;
        }

        Console.WriteLine("[1/2] Analyze complete.");
        Console.WriteLine(
            $"  Duration: {metrics.Duration.TotalSeconds:F2}s ({audio.Length:N0} bytes), " +
            $"rms {metrics.Rms:F5}, peak {metrics.Peak:F5}, " +
            $"active ratio {(metrics.ActiveSampleRatio * 100):F1}%, " +
            $"non-zero sample: {(metrics.HasAnyNonZeroSample ? "yes" : "no")}, " +
            $"likely silence: {(metrics.IsLikelySilence ? "yes" : "no")}");

        if (audio.Length == 0)
        {
            Console.WriteLine("[2/2] No audio data captured. Playback skipped.");
            return false;
        }

        if (options.NoPlayback)
        {
            if (options.SavePath is null)
            {
                Console.WriteLine("[2/2] Capture saved only in memory (no playback requested).");
            }
            else
            {
                File.WriteAllBytes(options.SavePath, audio);
                Console.WriteLine($"[2/2] Audio saved to: {options.SavePath}");
            }

            return true;
        }

        try
        {
            Console.WriteLine("[2/2] Playing captured audio...");
            await StartPlaybackAsync(audio, outputIndex, outputVolume, cancellationToken: default);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[2/2] Playback failed.");
            Console.WriteLine($"  {ex.Message}");
            return false;
        }

        if (options.SavePath is not null)
        {
            File.WriteAllBytes(options.SavePath, audio);
            Console.WriteLine($"[2/2] Audio saved to: {options.SavePath}");
        }

        Console.WriteLine("[2/2] Validation succeeded.");
        return true;
    }

    private static async Task StartPlaybackAsync(
        byte[] audioData,
        int outputDeviceIndex,
        float outputVolume,
        CancellationToken cancellationToken)
    {
        await using var playbackStream = new MemoryStream(audioData);
        using var playbackReader = new WaveFileReader(playbackStream);
        var output = BuildPlaybackOutput(outputDeviceIndex, out var outputSummary);
        using (output)
        {
            output.Volume = Math.Clamp(outputVolume, 0f, 1f);
            output.Init(playbackReader);
            Console.WriteLine($"  output device: {outputSummary}");
            Console.WriteLine($"  output volume: {Math.Clamp(outputVolume, 0f, 1f):P0}");
            output.Play();
            while (!cancellationToken.IsCancellationRequested &&
                (output.PlaybackState is PlaybackState.Playing or PlaybackState.Paused))
            {
                await Task.Delay(40, cancellationToken);
            }
        }
    }

    private static async Task PlayDingToneAsync(
        int outputDeviceIndex,
        float outputVolume,
        CancellationToken cancellationToken)
    {
        using var output = BuildPlaybackOutput(outputDeviceIndex, out var outputSummary);

        var tone = new SignalGenerator(44100, 1)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 1000,
            Gain = 0.4f
        };

        var dingProvider = new OffsetSampleProvider(tone)
        {
            Take = TimeSpan.FromMilliseconds(DefaultDingDurationMs)
        };

        output.Volume = Math.Clamp(outputVolume, 0f, 1f);
        output.Init(new SampleToWaveProvider16(dingProvider));
        Console.WriteLine($"  output device: {outputSummary}");
        Console.WriteLine($"  output volume: {Math.Clamp(outputVolume, 0f, 1f):P0}");

        output.Play();
        while (!cancellationToken.IsCancellationRequested &&
            (output.PlaybackState is PlaybackState.Playing or PlaybackState.Paused))
        {
            await Task.Delay(40, cancellationToken);
        }

        if (cancellationToken.IsCancellationRequested)
            output.Stop();
    }

    private static WaveOutEvent BuildPlaybackOutput(int outputDeviceIndex, out string outputSummary)
    {
        var outputDeviceCount = WaveOut.DeviceCount;

        if (outputDeviceIndex < 0)
        {
            outputSummary = "system default output";
            return new WaveOutEvent();
        }

        if (outputDeviceIndex >= outputDeviceCount)
        {
            Console.WriteLine(
                $"  Requested output device {outputDeviceIndex} is not available " +
                $"(available count {outputDeviceCount}); using system default output.");
            outputSummary = "system default output";
            return new WaveOutEvent();
        }

        try
        {
            outputSummary = $"index {outputDeviceIndex} ({GetOutputDeviceName(outputDeviceIndex)})";
            return new WaveOutEvent { DeviceNumber = outputDeviceIndex };
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"  Failed to initialize requested output {outputDeviceIndex}; " +
                $"falling back to system default output. ({ex.Message})");
            outputSummary = "system default output";
            return new WaveOutEvent();
        }
    }

    private static bool ValidateOutputDevice(int outputDeviceIndex)
    {
        var outputCount = WaveOut.DeviceCount;
        return outputDeviceIndex >= 0 && outputDeviceIndex < outputCount;
    }

    private static bool ValidateInputDevice(int inputDeviceIndex)
    {
        var inputCount = WaveIn.DeviceCount;
        return inputDeviceIndex == AppConfig.DefaultAudioDeviceIndex || (inputDeviceIndex >= 0 && inputDeviceIndex < inputCount);
    }

    private static void PrintDeviceSelectionWarnings(DebugOptions options)
    {
        if (options.InputIndex != AppConfig.DefaultAudioDeviceIndex &&
            !ValidateInputDevice(options.InputIndex))
        {
            Console.WriteLine($"[WARN] Input index {options.InputIndex} is outside available devices.");
        }

        if (options.OutputIndex != AppConfig.DefaultAudioDeviceIndex &&
            !ValidateOutputDevice(options.OutputIndex))
        {
            Console.WriteLine($"[WARN] Output index {options.OutputIndex} is outside available devices.");
        }
    }

    private static void PrintDeviceTable()
    {
        Console.WriteLine("=== Audio Device Discovery ===");

        var inputCount = WaveIn.DeviceCount;
        var outputCount = WaveOut.DeviceCount;

        Console.WriteLine("Input devices:");
        if (inputCount <= 0)
        {
            Console.WriteLine("  No input devices found.");
        }
        else
        {
            for (var deviceIndex = 0; deviceIndex < inputCount; deviceIndex++)
            {
                Console.WriteLine($"  [{deviceIndex}] {GetInputDeviceName(deviceIndex)}");
            }
        }

        Console.WriteLine("Output devices:");
        if (outputCount <= 0)
        {
            Console.WriteLine("  No output devices found.");
        }
        else
        {
            for (var deviceIndex = 0; deviceIndex < outputCount; deviceIndex++)
            {
                Console.WriteLine($"  [{deviceIndex}] {GetOutputDeviceName(deviceIndex)}");
            }
        }

        Console.WriteLine($"Input default: {GetInputDeviceDefaultIndex()}");
        Console.WriteLine($"Output default: {GetOutputDeviceDefaultIndex()}");
        Console.WriteLine();
    }

    private static string GetInputDeviceName(int deviceIndex)
    {
        try
        {
            return WaveIn.GetCapabilities(deviceIndex).ProductName;
        }
        catch
        {
            return $"index {deviceIndex}";
        }
    }

    private static string GetOutputDeviceName(int deviceIndex)
    {
        try
        {
            return WaveOut.GetCapabilities(deviceIndex).ProductName;
        }
        catch
        {
            return $"index {deviceIndex}";
        }
    }

    private static string GetInputDeviceDefaultIndex()
    {
        try
        {
            return WaveIn.DeviceCount > 0 ? "system default" : "none";
        }
        catch
        {
            return "none";
        }
    }

    private static string GetOutputDeviceDefaultIndex()
    {
        try
        {
            return WaveOut.DeviceCount > 0 ? "system default" : "none";
        }
        catch
        {
            return "none";
        }
    }

    private static void ApplyConfigDefaults(ref DebugOptions options)
    {
        var config = AppConfig.Load();

        if (options.InputIndex == -1 && string.IsNullOrWhiteSpace(options.InputName))
        {
            options = options with
            {
                InputIndex = config.MicrophoneInputDeviceIndex,
                InputName = config.MicrophoneInputDeviceName
            };
        }

        if (options.OutputIndex == -1)
            options = options with { OutputIndex = config.AudioOutputDeviceIndex };
    }

    private static DebugOptions ParseOptions(string[] args)
    {
        var options = new DebugOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg.ToLowerInvariant())
            {
                case "-h":
                case "--help":
                    options = options with { Help = true };
                    return options;
                case "-l":
                case "--list":
                case "--list-devices":
                    options = options with { ListOnly = true };
                    break;
                case "--from-config":
                    options = options with { FromConfig = true };
                    break;
                case "--ding":
                    options = options with { Ding = true };
                    break;
                case "--no-playback":
                    options = options with { NoPlayback = true };
                    break;
                case "--output-volume":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--output-volume requires a percentage value.");
                    if (!float.TryParse(
                        args[++i],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var outputVolume))
                    {
                        throw new ArgumentException("Invalid output-volume value.");
                    }
                    if (outputVolume < 0f || outputVolume > 100f)
                        throw new ArgumentException("output-volume must be between 0 and 100.");
                    options = options with { OutputVolume = outputVolume / 100f };
                    break;
                case "--save":
                case "-s":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--save requires a file path.");
                    options = options with { SavePath = args[++i] };
                    break;
                case "--duration":
                case "--duration-ms":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{arg} requires milliseconds.");
                    if (!int.TryParse(
                            args[++i],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var durationMs))
                    {
                        throw new ArgumentException("Invalid duration value.");
                    }
                    if (durationMs < MinCaptureDurationMs || durationMs > MaxCaptureDurationMs)
                        throw new ArgumentException(
                            $"duration-ms must be between {MinCaptureDurationMs} and {MaxCaptureDurationMs}.");
                    options = options with { DurationMs = durationMs };
                    break;
                case "--input":
                case "--input-index":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--input requires an index.");
                    if (!int.TryParse(
                        args[++i],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var inputIndex))
                    {
                        throw new ArgumentException("Invalid input index.");
                    }
                    options = options with { InputIndex = inputIndex };
                    break;
                case "--input-name":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--input-name requires a string.");
                    options = options with { InputName = args[++i] };
                    break;
                case "--output":
                case "--output-index":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--output requires an index.");
                    if (!int.TryParse(
                        args[++i],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var outputIndex))
                    {
                        throw new ArgumentException("Invalid output index.");
                    }
                    options = options with { OutputIndex = outputIndex };
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {arg}");
            }
        }

        return options;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("VoiceType Audio Debug Tool");
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project tools/audio-debug -- [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --help, -h                    Show help.");
        Console.WriteLine("  --list, -l                    List input/output devices and exit.");
        Console.WriteLine("  --from-config                  Load VoiceType defaults (saved mic/output indexes).");
        Console.WriteLine("  --ding                         Play a short output test tone (no capture).");
        Console.WriteLine("  --input-index, --input <n>     Preferred microphone input index (default: -1).");
        Console.WriteLine("  --input-name <name>            Preferred microphone input name.");
        Console.WriteLine("  --output-index, --output <n>   Output index for playback (default: -1/system default).");
        Console.WriteLine("  --duration-ms <ms>             Capture window in ms (250 - 300000).");
        Console.WriteLine("  --output-volume <pct>          Playback volume percentage (0 - 100).");
        Console.WriteLine("  --no-playback                  Skip playback of captured audio.");
        Console.WriteLine("  --save <path>                  Write captured WAV to file.");
        Console.WriteLine();
    }

    private sealed record DebugOptions(
        bool Help = false,
        bool ListOnly = false,
        bool FromConfig = false,
        bool NoPlayback = false,
        bool Ding = false,
        int InputIndex = -1,
        string? InputName = null,
        int OutputIndex = -1,
        int DurationMs = DefaultCaptureDurationMs,
        float OutputVolume = 1f,
        string? SavePath = null);
}
