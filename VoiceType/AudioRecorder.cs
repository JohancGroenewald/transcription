using NAudio.Wave;

namespace VoiceType;

public sealed class AudioCaptureSelectionState
{
    public static AudioCaptureSelectionState Empty { get; } = new();

    public int RequestedCaptureDeviceIndex { get; init; } = AppConfig.DefaultAudioDeviceIndex;
    public string RequestedCaptureDeviceName { get; init; } = string.Empty;
    public int ActiveCaptureDeviceIndex { get; set; } = AppConfig.DefaultAudioDeviceIndex;
    public string ActiveCaptureDeviceName { get; set; } = string.Empty;
    public string SelectionReason { get; set; } = "not started";
    public bool UsedFallback { get; set; }
    public string LastError { get; set; } = string.Empty;
    public IReadOnlyList<string> Attempts { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DeviceSnapshot { get; set; } = Array.Empty<string>();

    public string RequestedSummary => DescribeCaptureDevice(RequestedCaptureDeviceIndex, RequestedCaptureDeviceName);
    public string ActiveSummary => DescribeCaptureDevice(ActiveCaptureDeviceIndex, ActiveCaptureDeviceName);
    public string SelectionSummary => $"{RequestedSummary} -> {ActiveSummary} ({SelectionReason}){(UsedFallback ? " [fallback]" : string.Empty)}";

    public static string DescribeCaptureDevice(int deviceIndex, string? deviceName)
    {
        if (deviceIndex == AppConfig.DefaultAudioDeviceIndex)
            return "system default";

        if (string.IsNullOrWhiteSpace(deviceName))
            return $"index {deviceIndex}";

        return $"{deviceName} (index {deviceIndex})";
    }
}

public readonly record struct AudioCaptureMetrics(
    TimeSpan Duration,
    double Rms,
    double Peak,
    double ActiveSampleRatio,
    bool HasAnyNonZeroSample)
{
    // Conservative silence gate to avoid transcribing pure noise/silence.
    public bool IsLikelySilence =>
        Duration < TimeSpan.FromMilliseconds(250)
        || (!HasAnyNonZeroSample
            || (Rms < 0.0025 && Peak < 0.012 && ActiveSampleRatio < 0.01));
}

public class AudioRecorder : IDisposable
{
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PostStopDrainTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly int[] PreferredSampleRates = { 16000, 24000, 32000, 44100, 48000 };
    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private WaveFormat? _waveFormat;
    private TaskCompletionSource<bool>? _recordingStopped;
    private bool _disposed;
    private bool _maxRecordingLimitReached;
    private long _maxRecordingBytes;
    private int _preferredCaptureDeviceIndex = AppConfig.DefaultAudioDeviceIndex;
    private string _preferredCaptureDeviceName = string.Empty;
    private AudioCaptureSelectionState _lastCaptureSelection = AudioCaptureSelectionState.Empty;

    public AudioCaptureMetrics LastCaptureMetrics { get; private set; }
    public AudioCaptureSelectionState LastCaptureSelection => _lastCaptureSelection;
    public event Action<int>? InputLevelChanged;

    public AudioRecorder(int preferredCaptureDeviceIndex = AppConfig.DefaultAudioDeviceIndex, string? preferredCaptureDeviceName = null)
    {
        _preferredCaptureDeviceIndex = AppConfig.NormalizeAudioDeviceIndex(preferredCaptureDeviceIndex);
        _preferredCaptureDeviceName = preferredCaptureDeviceName?.Trim() ?? string.Empty;
    }

    public void ConfigureCaptureDevice(int preferredCaptureDeviceIndex, string? preferredCaptureDeviceName)
    {
        if (_waveIn != null)
            throw new InvalidOperationException("Cannot change capture device while recording is active.");

        _preferredCaptureDeviceIndex = AppConfig.NormalizeAudioDeviceIndex(preferredCaptureDeviceIndex);
        _preferredCaptureDeviceName = preferredCaptureDeviceName?.Trim() ?? string.Empty;
    }

    public void Start()
    {
        ThrowIfDisposed();
        if (_waveIn != null)
            throw new InvalidOperationException("Already recording.");

        _lastCaptureSelection = new AudioCaptureSelectionState
        {
            RequestedCaptureDeviceIndex = _preferredCaptureDeviceIndex,
            RequestedCaptureDeviceName = _preferredCaptureDeviceName,
            DeviceSnapshot = GetCaptureDeviceSnapshot()
        };
        LastCaptureMetrics = default;
        _audioBuffer = new MemoryStream();
        _recordingStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _maxRecordingLimitReached = false;
        _maxRecordingBytes = 0;

        try
        {
            StartRecordingWithFallback();
        }
        catch
        {
            CleanupRecorder();
            throw;
        }
    }

    private void StartRecordingWithFallback()
    {
        const string RequestedIndexStrategy = "requested index";
        const string RequestedNameStrategy = "requested name";

        var deviceCount = WaveIn.DeviceCount;
        if (deviceCount <= 0)
        {
            throw new InvalidOperationException(
                "No microphone input devices are available. " +
                "Connect or enable a microphone, then retry.");
        }

        var attempts = new List<string>();
        Exception? lastError = null;
        var selectedDeviceIndexes = GetPreferredCaptureDeviceCandidates(deviceCount);

        foreach (var candidate in selectedDeviceIndexes)
        {
            var deviceIndex = candidate.DeviceIndex;
            var deviceName = TryGetCaptureDeviceName(deviceIndex);
            var isRequested = string.Equals(candidate.Strategy, RequestedIndexStrategy, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Strategy, RequestedNameStrategy, StringComparison.OrdinalIgnoreCase);

            foreach (var sampleRate in PreferredSampleRates)
            {
                WaveInEvent? waveIn = null;
                try
                {
                    waveIn = new WaveInEvent
                    {
                        DeviceNumber = deviceIndex,
                        WaveFormat = new WaveFormat(sampleRate, 16, 1)
                    };

                    waveIn.DataAvailable += OnDataAvailable;
                    waveIn.RecordingStopped += OnRecordingStopped;
                    waveIn.StartRecording();

                    _waveIn = waveIn;
                    _waveFormat = waveIn.WaveFormat;
                    _maxRecordingBytes = (long)_waveFormat.AverageBytesPerSecond * (long)MaxRecordingDuration.TotalSeconds;

                    _lastCaptureSelection.ActiveCaptureDeviceIndex = deviceIndex;
                    _lastCaptureSelection.ActiveCaptureDeviceName = deviceName;
                    _lastCaptureSelection.SelectionReason = candidate.Strategy;
                    _lastCaptureSelection.UsedFallback = !isRequested;
                    _lastCaptureSelection.Attempts = attempts.ToArray();
                    _lastCaptureSelection.LastError = string.Empty;
                    Log.Info(
                        $"Recording started on device {deviceIndex} ({deviceName}) at {_waveFormat.SampleRate} Hz" +
                        $" [requested: {_lastCaptureSelection.RequestedSummary}, active: {_lastCaptureSelection.ActiveSummary}, strategy: {_lastCaptureSelection.SelectionReason}]");
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    attempts.Add($"device {deviceIndex} ({deviceName}) @ {sampleRate}Hz: {ex.Message}");
                    _lastCaptureSelection.LastError = ex.Message;
                    // Release failed capture attempt before moving to the next format/device combo.
                    // Event handlers are attached only to this local instance, so disposal is safe here.
                    if (waveIn != null)
                    {
                        try
                        {
                            waveIn.Dispose();
                        }
                        catch
                        {
                            // Ignore disposal failures while switching attempts.
                        }
                    }
                }
            }
        }

        _lastCaptureSelection.UsedFallback = true;
        _lastCaptureSelection.SelectionReason = "failed to start";
        _lastCaptureSelection.Attempts = attempts.ToArray();
        _lastCaptureSelection.ActiveCaptureDeviceIndex = AppConfig.DefaultAudioDeviceIndex;
        _lastCaptureSelection.ActiveCaptureDeviceName = string.Empty;
        _lastCaptureSelection.Attempts = attempts.ToArray();
        throw BuildStartFailureException(_lastCaptureSelection, lastError);
    }

    private IEnumerable<(int DeviceIndex, string DeviceName, string Strategy)> GetPreferredCaptureDeviceCandidates(int deviceCount)
    {
        var seen = new HashSet<int>();

        if (_preferredCaptureDeviceIndex >= 0 &&
            _preferredCaptureDeviceIndex < deviceCount &&
            seen.Add(_preferredCaptureDeviceIndex))
        {
            yield return (
                _preferredCaptureDeviceIndex,
                TryGetCaptureDeviceName(_preferredCaptureDeviceIndex),
                "requested index");
        }

        if (!string.IsNullOrWhiteSpace(_preferredCaptureDeviceName))
        {
            for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                if (_preferredCaptureDeviceIndex == deviceIndex)
                    continue;

                var candidateName = TryGetCaptureDeviceName(deviceIndex);
                if (string.Equals(candidateName, _preferredCaptureDeviceName, StringComparison.OrdinalIgnoreCase) &&
                    seen.Add(deviceIndex))
                {
                    yield return (deviceIndex, candidateName, "requested name");
                }
            }
        }

        for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
        {
            if (seen.Add(deviceIndex))
                yield return (deviceIndex, TryGetCaptureDeviceName(deviceIndex), "fallback");
        }
    }

    private static IReadOnlyList<string> GetCaptureDeviceSnapshot()
    {
        var deviceCount = WaveIn.DeviceCount;
        if (deviceCount <= 0)
            return Array.Empty<string>();

        var snapshot = new List<string>(deviceCount);
        for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
        {
            snapshot.Add($"{deviceIndex}: {TryGetCaptureDeviceName(deviceIndex)}");
        }

        return snapshot;
    }

    private static string TryGetCaptureDeviceName(int deviceIndex)
    {
        try
        {
            var capabilities = WaveIn.GetCapabilities(deviceIndex);
            return capabilities.ProductName;
        }
        catch
        {
            return $"index {deviceIndex}";
        }
    }

    private static Exception BuildStartFailureException(AudioCaptureSelectionState captureSelection, Exception? lastError)
    {
        var attemptText = string.Join(" | ", captureSelection.Attempts);
        var detailText = !string.IsNullOrWhiteSpace(attemptText)
            ? $"Tried: {attemptText}"
            : "No capture format/device combinations succeeded.";
        var baseMessage = "Unable to start microphone capture. Check Windows microphone permissions and device availability.";
        var rootMessage = lastError?.GetType().Name ?? "UnknownError";
        return new InvalidOperationException(
            $"{baseMessage} {detailText} (last error: {rootMessage}: {lastError?.Message})");
    }

    /// <summary>
    /// Stops recording and returns the WAV audio as a byte array.
    /// </summary>
    public byte[] Stop()
    {
        ThrowIfDisposed();
        if (_waveIn == null || _audioBuffer == null || _waveFormat == null)
            throw new InvalidOperationException("Not currently recording.");

        var waveIn = _waveIn;
        var waveFormat = _waveFormat;
        var stopped = _recordingStopped;

        waveIn.StopRecording();
        var stopCompleted = stopped != null && stopped.Task.Wait(PostStopDrainTimeout);
        if (stopped != null && stopCompleted)
        {
            stopped.Task.GetAwaiter().GetResult();
        }
        else if (stopped != null)
        {
            Log.Info($"Recording stop timeout ({PostStopDrainTimeout.TotalMilliseconds:F0} ms). Proceeding with captured audio.");
        }

        byte[] rawAudio;
        lock (_sync)
        {
            rawAudio = _audioBuffer.ToArray();
        }

        LastCaptureMetrics = AnalyzeRawPcm16Mono(rawAudio, waveFormat);
        CleanupRecorder();

        // Write a proper WAV file by letting WaveFileWriter handle the header
        using var outputStream = new MemoryStream();
        using (var writer = new WaveFileWriter(outputStream, waveFormat))
        {
            writer.Write(rawAudio, 0, rawAudio.Length);
        }
        // Disposing WaveFileWriter finalizes the WAV header correctly

        return outputStream.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            // Best effort shutdown
        }

        CleanupRecorder();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var shouldStopRecording = false;
        lock (_sync)
        {
            var buffer = _audioBuffer;
            if (buffer == null)
                return;

            buffer.Write(e.Buffer, 0, e.BytesRecorded);

            if (!_maxRecordingLimitReached &&
                _maxRecordingBytes > 0 &&
                buffer.Length >= _maxRecordingBytes)
            {
                _maxRecordingLimitReached = true;
                shouldStopRecording = true;
            }
        }

        if (shouldStopRecording)
        {
            Log.Info($"Maximum recording duration reached ({MaxRecordingDuration.TotalSeconds:0.0} seconds). Auto-stopping.");
            try
            {
                _waveIn?.StopRecording();
            }
            catch (Exception ex)
            {
                Log.Error("Failed to auto-stop recording after duration limit was reached.", ex);
            }
        }

        var levelPercent = CalculateInputLevelPercent(e.Buffer, e.BytesRecorded);
        try
        {
            InputLevelChanged?.Invoke(levelPercent);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to publish input level update.", ex);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            _recordingStopped?.TrySetException(e.Exception);
        else
            _recordingStopped?.TrySetResult(true);
    }

    private void CleanupRecorder()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _audioBuffer?.Dispose();
        _audioBuffer = null;
        _waveFormat = null;
        _recordingStopped = null;
        _maxRecordingLimitReached = false;
        _maxRecordingBytes = 0;
    }

    private static AudioCaptureMetrics AnalyzeRawPcm16Mono(byte[] rawAudio, WaveFormat waveFormat)
    {
        if (rawAudio.Length < 2 || waveFormat.SampleRate <= 0)
            return default;

        if (waveFormat.BitsPerSample != 16)
            return default;

        var bytesPerSample = waveFormat.BitsPerSample / 8;
        var blockSize = bytesPerSample * Math.Max(1, waveFormat.Channels);
        if (rawAudio.Length < blockSize || blockSize <= 0)
            return default;

        var sampleCount = rawAudio.Length / blockSize;
        if (sampleCount == 0)
            return default;

        const int activeThreshold = 512; // ~1.6% of full-scale

        long sumSquares = 0;
        var peak = 0;
        var activeSamples = 0;
        var hasAnyNonZeroSample = false;

        for (var frameIndex = 0; frameIndex < sampleCount; frameIndex++)
        {
            var frameOffset = frameIndex * blockSize;
            for (var channelIndex = 0; channelIndex < waveFormat.Channels; channelIndex++)
            {
                var sampleOffset = frameOffset + (channelIndex * bytesPerSample);
                if (sampleOffset + bytesPerSample > rawAudio.Length)
                    break;

                var sample = (short)(rawAudio[sampleOffset] | (rawAudio[sampleOffset + 1] << 8));
                var sampleValue = sample;
                var abs = Math.Abs((int)sampleValue);

                if (abs != 0)
                    hasAnyNonZeroSample = true;
                if (abs > peak)
                    peak = abs;
                if (abs >= activeThreshold)
                    activeSamples++;

                sumSquares += (long)sampleValue * sampleValue;
            }
        }

        var duration = TimeSpan.FromSeconds(sampleCount / (double)waveFormat.SampleRate);
        var rms = Math.Sqrt(sumSquares / (double)Math.Max(1, sampleCount * waveFormat.Channels)) / short.MaxValue;
        var peakNormalized = peak / (double)short.MaxValue;
        var activeRatio = activeSamples / (double)Math.Max(1, sampleCount * waveFormat.Channels);

        return new AudioCaptureMetrics(duration, rms, peakNormalized, activeRatio, hasAnyNonZeroSample);
    }

    private static int CalculateInputLevelPercent(byte[] pcm16Buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
            return 0;

        var chunkBytes = Math.Min(bytesRecorded, pcm16Buffer.Length) & ~1;
        var sampleCount = chunkBytes / 2;
        if (sampleCount == 0)
            return 0;

        long sumSquares = 0;
        var peak = 0;
        var hasAnyNonZeroSample = false;

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = i * 2;
            var sample = (short)(pcm16Buffer[offset] | (pcm16Buffer[offset + 1] << 8));
            var sampleValue = (int)sample;
            var abs = Math.Abs(sampleValue);

            if (abs > 0)
                hasAnyNonZeroSample = true;
            if (abs > peak)
                peak = abs;

            sumSquares += (long)sampleValue * sampleValue;
        }

        if (!hasAnyNonZeroSample)
            return 0;

        var rms = Math.Sqrt(sumSquares / (double)sampleCount) / short.MaxValue;
        var peakNormalized = peak / (double)short.MaxValue;

        // Blend peak and RMS for a responsive but stable signal envelope.
        var blended = Math.Max(peakNormalized, rms * 2.8);

        // Make the listening meter responsive on low-level inputs found on some PCs.
        var db = 20 * Math.Log10(Math.Max(blended, 1e-8));
        var minDb = -78.0;
        var maxDb = -20.0;
        var normalizedDb = Math.Clamp((db - minDb) / (maxDb - minDb), 0, 1);
        var percent = (int)Math.Round(normalizedDb * 100);

        return Math.Max(12, percent);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioRecorder));
    }
}
