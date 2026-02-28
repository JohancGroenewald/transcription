using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using VoiceType;

namespace VoiceTypeAudioDebug;

internal static class Program
{
    private const int DefaultCaptureDurationMs = 3000;
    private const int DefaultDingDurationMs = 900;
    private const string DefaultSaveFile = "audio-debug-test.wav";
    private const float DefaultOutputVolume = 0.5f;
    private const float DefaultInputGain = 1f;
    private const float DefaultInputHardwareVolumePercent = -1f;
    private const int MinCaptureDurationMs = 250;
    private const int MaxCaptureDurationMs = 300000;
    private const int MinLoopCount = 1;
    private const int MaxLoopCount = 20;
    private const float MinInputGainPercent = 0f;
    private const float MaxInputGainPercent = 400f;
    private const float MinInputHardwareVolumePercent = 0f;
    private const float MaxInputHardwareVolumePercent = 100f;

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

            var result = options.Play
                ? await RunPlayFileAsync(options)
                : options.Ding
                ? await RunDingTestAsync(options)
                : await RunMicValidationLoopAsync(options);
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
            var dingAudio = BuildDingAudio(options.OutputVolume);
            var shouldSaveOutput = options.SaveOutput || options.SaveInput;
            if (shouldSaveOutput)
            {
                SaveAudioFile("output", dingAudio, options.SaveOutputPath ?? options.SaveInputPath);
            }
            await StartPlaybackAsync(dingAudio, options.OutputIndex, options.OutputVolume, cancellationToken: default);
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

    private static async Task<bool> RunMicValidationLoopAsync(DebugOptions options)
    {
        if (options.LoopCount <= 1)
        {
            return await RunMicValidationAsync(options);
        }

        Console.WriteLine($"=== VoiceType audio validation loop ({options.LoopCount}x) ===");
        Console.WriteLine($"Input mic gain: {options.InputGain * 100:F0}%");
        Console.WriteLine();

        for (var loopIndex = 1; loopIndex <= options.LoopCount; loopIndex++)
        {
            Console.WriteLine($"-- Loop {loopIndex} of {options.LoopCount} --");
            var loopResult = await RunMicValidationAsync(options, loopIndex, options.LoopCount);
            if (!loopResult)
            {
                Console.WriteLine($"Loop {loopIndex} failed. Stopping.");
                return false;
            }

            if (loopIndex < options.LoopCount)
            {
                Console.WriteLine();
            }
        }

        return true;
    }

    private static async Task<bool> RunPlayFileAsync(DebugOptions options)
    {
        var requestedOutputSummary = options.OutputIndex < 0
            ? "system default output"
            : $"index {options.OutputIndex}";
        var path = ResolveSavePath(options.PlayFilePath);

        Console.WriteLine("=== VoiceType audio file playback ===");
        Console.WriteLine($"Output device request: {requestedOutputSummary}");
        Console.WriteLine($"Audio file: {path}");
        Console.WriteLine();

        if (!File.Exists(path))
        {
            Console.WriteLine("[1/1] Audio file not found.");
            Console.WriteLine($"  {path}");
            return false;
        }

        try
        {
            Console.WriteLine("[1/1] Playing file...");
            var audio = await File.ReadAllBytesAsync(path);
            await StartPlaybackAsync(audio, options.OutputIndex, options.OutputVolume, cancellationToken: default);
            Console.WriteLine("[1/1] File playback succeeded.");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[1/1] File playback failed.");
            Console.WriteLine($"  {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> RunMicValidationAsync(DebugOptions options, int loopIndex = 1, int loopCount = 1)
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

        if (loopCount > 1)
        {
            Console.WriteLine($"=== VoiceType audio validation (loop {loopIndex}/{loopCount}) ===");
        }
        else
        {
            Console.WriteLine("=== VoiceType audio validation ===");
        }

        Console.WriteLine($"Input device request: {requestedInputSummary}");
        Console.WriteLine($"Output device request: {requestedOutputSummary}");
        Console.WriteLine($"Capture duration: {durationMs} ms");
        Console.WriteLine($"Input gain: {options.InputGain * 100:F0}%");
        Console.WriteLine(
            InputVolumeChangesRequested(options.InputHardwareVolumePercent)
                ? $"Input hardware volume request: {options.InputHardwareVolumePercent:F0}%"
                : "Input hardware volume: unchanged");
        Console.WriteLine();

        MMDevice? captureVolumeDevice = null;
        var originalCaptureVolumePercent = DefaultInputHardwareVolumePercent;

        try
        {
            SetCaptureInputHardwareVolume(
                inputIndex,
                inputName,
                options.InputHardwareVolumePercent,
                out captureVolumeDevice,
                out originalCaptureVolumePercent);

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

            if (!InputGainsAreEqual(options.InputGain, DefaultInputGain))
            {
                try
                {
                    audio = ApplyInputGain(audio, options.InputGain);
                    Console.WriteLine($"      Mic gain applied: {options.InputGain * 100:F0}%");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"      Failed to apply mic gain ({ex.Message}). Using raw capture.");
                }
            }

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
            if (!options.SaveInput)
            {
                Console.WriteLine("[2/2] Capture saved only in memory (no playback requested).");
            }
            else
            {
                SaveAudioFile("input", audio, options.SaveInputPath, loopIndex, loopCount);
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

        if (options.SaveInput)
            SaveAudioFile("input", audio, options.SaveInputPath, loopIndex, loopCount);

        if (options.SaveOutput)
        {
            SaveAudioFile("output", audio, options.SaveOutputPath, loopIndex, loopCount);
        }

        Console.WriteLine("[2/2] Validation succeeded.");
        return true;
        }
        finally
        {
            if (captureVolumeDevice is not null &&
                InputVolumeChangesRequested(options.InputHardwareVolumePercent))
            {
                RestoreCaptureInputHardwareVolume(captureVolumeDevice, originalCaptureVolumePercent);
            }
        }
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

    private static byte[] BuildDingAudio(float outputVolume)
    {
        const int dingSampleRate = 44100;
        const int channels = 1;

        var tone = new SignalGenerator(dingSampleRate, channels)
        {
            Type = SignalGeneratorType.Sin,
            Frequency = 1000,
            Gain = 0.4f * Math.Clamp(outputVolume, 0f, 1f)
        };

        var dingProvider = new OffsetSampleProvider(tone)
        {
            Take = TimeSpan.FromMilliseconds(DefaultDingDurationMs)
        };

        var dingWaveProvider = new SampleToWaveProvider16(dingProvider);
        using var dingOutputStream = new MemoryStream();
        using (var dingWaveWriter = new WaveFileWriter(dingOutputStream, dingWaveProvider.WaveFormat))
        {
            var buffer = new byte[4096];
            int read;
            while ((read = dingWaveProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                dingWaveWriter.Write(buffer, 0, read);
            }
        }

        return dingOutputStream.ToArray();
    }

    private static void SaveAudioFile(string stage, byte[] audioData, string? explicitPath)
    {
        var outputPath = ResolveSavePath(explicitPath);
        File.WriteAllBytes(outputPath, audioData);
        Console.WriteLine($"  Saved {stage} audio to: {outputPath}");
    }

    private static void SaveAudioFile(
        string stage,
        byte[] audioData,
        string? explicitPath,
        int loopIndex,
        int loopCount)
    {
        var outputPath = ResolveSavePath(explicitPath, loopIndex, loopCount);
        File.WriteAllBytes(outputPath, audioData);
        Console.WriteLine($"  Saved {stage} audio to: {outputPath}");
    }

    private static byte[] ApplyInputGain(byte[] sourceAudio, float inputGain)
    {
        if (sourceAudio.Length == 0)
        {
            return sourceAudio;
        }

        var clampedGain = Math.Clamp(inputGain, 0f, 4f);
        if (InputGainsAreEqual(clampedGain, 1f))
        {
            return sourceAudio;
        }

        using var sourceStream = new MemoryStream(sourceAudio);
        using var reader = new WaveFileReader(sourceStream);
        var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
        {
            Volume = clampedGain
        };
        using var outputStream = new MemoryStream();
        using (var writer = new WaveFileWriter(outputStream, reader.WaveFormat))
        {
            using var convertedProvider = new SampleToWaveProvider16(volumeProvider);
            var buffer = new byte[4096];
            int read;
            while ((read = convertedProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                writer.Write(buffer, 0, read);
            }
        }

        return outputStream.ToArray();
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

    private static void SetCaptureInputHardwareVolume(
        int inputDeviceIndex,
        string? inputDeviceName,
        float requestedInputVolumePercent,
        out MMDevice? selectedDevice,
        out float originalVolumePercent)
    {
        selectedDevice = null;
        originalVolumePercent = DefaultInputHardwareVolumePercent;

        if (!InputVolumeChangesRequested(requestedInputVolumePercent))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);

            var selectedDeviceId = ResolveCaptureDeviceIdByNameOrIndex(
                captureDevices,
                inputDeviceIndex,
                ResolveInputDeviceName(inputDeviceIndex, inputDeviceName));

            if (string.IsNullOrWhiteSpace(selectedDeviceId))
            {
                return;
            }

            var matchingDevice = enumerator.GetDevice(selectedDeviceId);
            if (matchingDevice is null)
            {
                return;
            }

            var endpointVolume = matchingDevice.AudioEndpointVolume;
            originalVolumePercent = endpointVolume.MasterVolumeLevelScalar * 100f;
            endpointVolume.MasterVolumeLevelScalar = Math.Clamp(
                requestedInputVolumePercent / 100f,
                0f,
                1f);

            selectedDevice = matchingDevice;
            Console.WriteLine(
                $"  Input hardware volume changed from {originalVolumePercent:F0}% to {requestedInputVolumePercent:F0}% on {matchingDevice.FriendlyName}");
        }
        catch
        {
        }
    }

    private static void RestoreCaptureInputHardwareVolume(
        MMDevice captureVolumeDevice,
        float originalVolumePercent)
    {
        try
        {
            if (originalVolumePercent < MinInputHardwareVolumePercent ||
                originalVolumePercent > MaxInputHardwareVolumePercent)
            {
                return;
            }

            captureVolumeDevice.AudioEndpointVolume.MasterVolumeLevelScalar = originalVolumePercent / 100f;
            Console.WriteLine(
                $"  Input hardware volume restored to {originalVolumePercent:F0}% on {captureVolumeDevice.FriendlyName}");
        }
        finally
        {
            captureVolumeDevice.Dispose();
        }
    }

    private static string? ResolveCaptureDeviceIdByNameOrIndex(
        MMDeviceCollection captureDevices,
        int inputDeviceIndex,
        string requestedInputDeviceName)
    {
        if (captureDevices.Count <= 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(requestedInputDeviceName))
        {
            if (inputDeviceIndex >= 0 && inputDeviceIndex < captureDevices.Count)
            {
                return captureDevices[inputDeviceIndex]?.ID;
            }

            return null;
        }

        var exactMatch = CaptureDeviceIdByName(captureDevices, requestedInputDeviceName, exactMatch: true);
        if (exactMatch is not null)
        {
            return exactMatch;
        }

        var partialMatch = CaptureDeviceIdByName(captureDevices, requestedInputDeviceName, exactMatch: false);
        if (partialMatch is not null)
        {
            return partialMatch;
        }

        if (inputDeviceIndex >= 0 && inputDeviceIndex < captureDevices.Count)
        {
            return captureDevices[inputDeviceIndex]?.ID;
        }

        return null;
    }

    private static string? CaptureDeviceIdByName(
        MMDeviceCollection captureDevices,
        string requestedInputDeviceName,
        bool exactMatch)
    {
        for (var captureDeviceIndex = 0; captureDeviceIndex < captureDevices.Count; captureDeviceIndex++)
        {
            var device = captureDevices[captureDeviceIndex];
            var friendlyName = device.FriendlyName;
            var isMatch = exactMatch
                ? string.Equals(requestedInputDeviceName, friendlyName, StringComparison.OrdinalIgnoreCase)
                : friendlyName.Contains(requestedInputDeviceName, StringComparison.OrdinalIgnoreCase);
            if (!isMatch)
            {
                continue;
            }

            return device.ID;
        }

        return null;
    }

    private static string ResolveInputDeviceName(int inputDeviceIndex, string? inputDeviceName)
    {
        if (!string.IsNullOrWhiteSpace(inputDeviceName))
        {
            return inputDeviceName!.Trim();
        }

        return GetInputDeviceName(inputDeviceIndex);
    }

    private static bool InputVolumeChangesRequested(float inputHardwareVolumePercent)
    {
        return inputHardwareVolumePercent >= MinInputHardwareVolumePercent &&
            inputHardwareVolumePercent <= MaxInputHardwareVolumePercent;
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

    private static bool IsOptionLikeValue(string value)
    {
        return value.StartsWith("-");
    }

    private static string ResolveSavePath(string? explicitPath)
    {
        return string.IsNullOrWhiteSpace(explicitPath) ? DefaultSaveFile : explicitPath;
    }

    private static string ResolveSavePath(string? explicitPath, int loopIndex, int loopCount)
    {
        var basePath = ResolveSavePath(explicitPath);
        if (loopCount <= 1)
        {
            return basePath;
        }

        var directory = Path.GetDirectoryName(basePath);
        var fileName = Path.GetFileNameWithoutExtension(basePath);
        var extension = Path.GetExtension(basePath);
        var suffixedFileName = $"{fileName}.loop{loopIndex:00}{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? suffixedFileName
            : Path.Combine(directory, suffixedFileName);
    }

    private static bool InputGainsAreEqual(float left, float right)
    {
        return Math.Abs(left - right) <= 0.0001f;
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
                case "--play":
                    if (i + 1 < args.Length && !IsOptionLikeValue(args[i + 1]))
                    {
                        options = options with { Play = true, PlayFilePath = args[++i] };
                    }
                    else
                    {
                        options = options with { Play = true };
                    }
                    break;
                case "--no-playback":
                    options = options with { NoPlayback = true };
                    break;
                case "--save-in":
                    if (i + 1 < args.Length && !IsOptionLikeValue(args[i + 1]))
                    {
                        options = options with { SaveInput = true, SaveInputPath = args[++i] };
                    }
                    else
                    {
                        options = options with { SaveInput = true };
                    }
                    break;
                case "--save-out":
                    if (i + 1 < args.Length && !IsOptionLikeValue(args[i + 1]))
                    {
                        options = options with { SaveOutput = true, SaveOutputPath = args[++i] };
                    }
                    else
                    {
                        options = options with { SaveOutput = true };
                    }
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
                    options = options with
                    {
                        SaveInput = true,
                        SaveInputPath = args[++i]
                    };
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
                case "--loop":
                case "--loops":
                case "--loop-count":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{arg} requires an integer count.");
                    if (!int.TryParse(
                        args[++i],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var loopCount))
                    {
                        throw new ArgumentException("Invalid loop count.");
                    }
                    if (loopCount < MinLoopCount || loopCount > MaxLoopCount)
                        throw new ArgumentException(
                            $"loop-count must be between {MinLoopCount} and {MaxLoopCount}.");
                    options = options with { LoopCount = loopCount };
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
                case "--input-gain":
                case "--mic-gain":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException("--input-gain requires a percentage.");
                    if (!float.TryParse(
                        args[++i],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var inputGain))
                    {
                        throw new ArgumentException("Invalid input-gain value.");
                    }
                    if (inputGain < MinInputGainPercent || inputGain > MaxInputGainPercent)
                        throw new ArgumentException(
                            $"input-gain must be between {MinInputGainPercent} and {MaxInputGainPercent}.");
                    options = options with { InputGain = inputGain / 100f };
                    break;
                case "--input-volume":
                case "--mic-volume":
                case "--record-volume":
                    if (i + 1 >= args.Length)
                        throw new ArgumentException($"{arg} requires a percentage.");
                    if (!float.TryParse(
                        args[++i],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var inputHardwareVolume))
                    {
                        throw new ArgumentException("Invalid input-volume value.");
                    }
                    if (inputHardwareVolume < MinInputHardwareVolumePercent ||
                        inputHardwareVolume > MaxInputHardwareVolumePercent)
                    {
                        throw new ArgumentException(
                            $"input-volume must be between {MinInputHardwareVolumePercent} and {MaxInputHardwareVolumePercent}.");
                    }
                    options = options with { InputHardwareVolumePercent = inputHardwareVolume };
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
        Console.WriteLine("  --play [path]                  Play default audio file (audio-debug-test.wav) or a custom path.");
        Console.WriteLine("  --save-in [path]               Save captured audio (default: audio-debug-test.wav in current directory).");
        Console.WriteLine("  --save-out [path]              Save playback output audio (default: audio-debug-test.wav in current directory).");
        Console.WriteLine("  --save, -s <path>              Legacy: save captured input audio (and ding output when --ding is used).");
        Console.WriteLine("  --input-index, --input <n>      Preferred microphone input index (default: -1).");
        Console.WriteLine("  --input-name <name>             Preferred microphone input name.");
        Console.WriteLine("  --input-gain <pct>              Software mic gain multiplier in percent (0 - 400, default 100).");
        Console.WriteLine("  --input-volume <pct>            Set input device master volume level (0 - 100, default unchanged).");
        Console.WriteLine("  --mic-volume <pct>              Alias for --input-volume.");
        Console.WriteLine("  --record-volume <pct>           Alias for --input-volume.");
        Console.WriteLine("  --loop, --loops <n>             Repeat capture/playback n times (1 - 20, default 1).");
        Console.WriteLine("  --output-index, --output <n>    Output index for playback (default: -1/system default).");
        Console.WriteLine("  --duration-ms <ms>              Capture window in ms (250 - 300000).");
        Console.WriteLine("  --output-volume <pct>           Playback volume percentage (0 - 100, default 50).");
        Console.WriteLine("  --no-playback                   Skip playback of captured audio.");
        Console.WriteLine();
    }

    private sealed record DebugOptions(
        bool Help = false,
        bool ListOnly = false,
        bool FromConfig = false,
        bool NoPlayback = false,
        bool Ding = false,
        bool Play = false,
        int InputIndex = -1,
        string? InputName = null,
        int LoopCount = 1,
        float InputGain = DefaultInputGain,
        int OutputIndex = -1,
        int DurationMs = DefaultCaptureDurationMs,
        float OutputVolume = DefaultOutputVolume,
        float InputHardwareVolumePercent = DefaultInputHardwareVolumePercent,
        bool SaveInput = false,
        bool SaveOutput = false,
        string? SaveInputPath = null,
        string? SaveOutputPath = null,
        string? PlayFilePath = null);
}
