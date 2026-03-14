using Microsoft.Extensions.Logging;
using NAudio.Wave;
using VoiceType2.Alpha2.Core;

namespace VoiceType2.Alpha2.Infrastructure;

public sealed class FakeAudioInputSource(ILogger<FakeAudioInputSource> logger) : IAudioInputSource
{
    private readonly ILogger<FakeAudioInputSource> _logger = logger;

    public Task<IAudioCaptureSession> StartAsync(AudioInputOptions options, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting fake audio capture session.");
        return Task.FromResult<IAudioCaptureSession>(new FakeAudioCaptureSession());
    }

    private sealed class FakeAudioCaptureSession : IAudioCaptureSession
    {
        private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
        private bool _completed;

        public CaptureDeviceSelection Selection { get; } = new()
        {
            RequestedDeviceIndex = -1,
            RequestedDeviceName = "fake",
            ActiveDeviceIndex = -1,
            ActiveDeviceName = "fake-sine-wave",
            SelectionReason = "fake"
        };

        public Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
        {
            if (_completed)
            {
                throw new InvalidOperationException("Capture session is already completed.");
            }

            _completed = true;

            const int sampleRate = 16000;
            const int channels = 1;
            var duration = Math.Clamp((DateTimeOffset.UtcNow - _startedUtc).TotalSeconds, 1.0, 3.0);
            var rawPcm = AudioProcessing.CreateSineWavePcm16Mono(sampleRate, duration, 440.0);
            var waveFormat = new WaveFormat(sampleRate, 16, channels);
            var wavAudio = AudioProcessing.EncodeWave(rawPcm, waveFormat);
            var metrics = AudioProcessing.AnalyzeRawPcm16Mono(rawPcm, waveFormat);

            return Task.FromResult(new AudioCaptureResult(
                wavAudio,
                new AudioCaptureSummary(
                    wavAudio.Length,
                    sampleRate,
                    channels,
                    metrics,
                    Selection)));
        }

        public Task CancelAsync(CancellationToken cancellationToken = default)
        {
            _completed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class WaveInAudioInputSource(ILogger<WaveInAudioInputSource> logger) : IAudioInputSource
{
    private static readonly int[] PreferredSampleRates = [16000, 24000, 32000, 44100, 48000];
    private readonly ILogger<WaveInAudioInputSource> _logger = logger;

    public Task<IAudioCaptureSession> StartAsync(AudioInputOptions options, CancellationToken cancellationToken = default)
    {
        var session = new WaveInCaptureSession(_logger, options);
        session.Start();
        return Task.FromResult<IAudioCaptureSession>(session);
    }

    private sealed class WaveInCaptureSession : IAudioCaptureSession
    {
        private readonly ILogger _logger;
        private readonly AudioInputOptions _options;
        private readonly object _sync = new();
        private readonly TaskCompletionSource<bool> _recordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private WaveInEvent? _waveIn;
        private MemoryStream? _rawAudio;
        private WaveFormat? _waveFormat;
        private bool _disposed;
        private bool _completed;
        private long _maxRecordingBytes;

        public WaveInCaptureSession(ILogger logger, AudioInputOptions options)
        {
            _logger = logger;
            _options = options;
            Selection = new CaptureDeviceSelection
            {
                RequestedDeviceIndex = options.PreferredDeviceIndex,
                RequestedDeviceName = options.PreferredDeviceName
            };
        }

        public CaptureDeviceSelection Selection { get; private set; }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(WaveInCaptureSession));
            _rawAudio = new MemoryStream();
            StartWithFallback();
        }

        public async Task<AudioCaptureResult> StopAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, typeof(WaveInCaptureSession));
            if (_completed)
            {
                throw new InvalidOperationException("Capture session is already completed.");
            }

            if (_waveIn is null || _rawAudio is null || _waveFormat is null)
            {
                throw new InvalidOperationException("Capture session is not active.");
            }

            _completed = true;
            _waveIn.StopRecording();
            await _recordingStopped.Task.WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);

            byte[] rawPcm;
            lock (_sync)
            {
                rawPcm = _rawAudio.ToArray();
            }

            var wavAudio = AudioProcessing.EncodeWave(rawPcm, _waveFormat);
            var metrics = AudioProcessing.AnalyzeRawPcm16Mono(rawPcm, _waveFormat);
            await DisposeAsync();

            return new AudioCaptureResult(
                wavAudio,
                new AudioCaptureSummary(
                    wavAudio.Length,
                    _waveFormat.SampleRate,
                    _waveFormat.Channels,
                    metrics,
                    Selection));
        }

        public async Task CancelAsync(CancellationToken cancellationToken = default)
        {
            if (_completed)
            {
                return;
            }

            _completed = true;
            if (_waveIn is not null)
            {
                _waveIn.StopRecording();
            }

            try
            {
                await _recordingStopped.Task.WaitAsync(TimeSpan.FromMilliseconds(750), cancellationToken);
            }
            catch
            {
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;

            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
                _waveIn = null;
            }

            _rawAudio?.Dispose();
            _rawAudio = null;
            _waveFormat = null;

            return ValueTask.CompletedTask;
        }

        private void StartWithFallback()
        {
            var deviceCount = WaveIn.DeviceCount;
            if (deviceCount <= 0)
            {
                throw new InvalidOperationException("No microphone input devices are available.");
            }

            Exception? lastError = null;
            foreach (var (deviceIndex, deviceName, selectionReason, usedFallback) in EnumerateCandidates(deviceCount))
            {
                foreach (var sampleRate in PreferredSampleRates)
                {
                    WaveInEvent? candidate = null;
                    try
                    {
                        candidate = new WaveInEvent
                        {
                            DeviceNumber = deviceIndex,
                            WaveFormat = new WaveFormat(sampleRate, 16, 1)
                        };

                        candidate.DataAvailable += OnDataAvailable;
                        candidate.RecordingStopped += OnRecordingStopped;
                        candidate.StartRecording();

                        _waveIn = candidate;
                        _waveFormat = candidate.WaveFormat;
                        _maxRecordingBytes =
                            (long)_waveFormat.AverageBytesPerSecond * Math.Max(1, _options.MaxDurationSeconds);

                        Selection = Selection with
                        {
                            ActiveDeviceIndex = deviceIndex,
                            ActiveDeviceName = deviceName,
                            SelectionReason = selectionReason,
                            UsedFallback = usedFallback,
                            LastError = string.Empty
                        };

                        _logger.LogInformation(
                            "WaveIn capture started on {Device} at {SampleRate} Hz.",
                            Selection.ActiveSummary,
                            _waveFormat.SampleRate);
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        Selection = Selection with { LastError = ex.Message };
                        try
                        {
                            candidate?.Dispose();
                        }
                        catch
                        {
                        }
                    }
                }
            }

            throw new InvalidOperationException(
                $"Unable to start microphone capture. Last error: {lastError?.Message ?? "unknown"}");
        }

        private IEnumerable<(int DeviceIndex, string DeviceName, string SelectionReason, bool UsedFallback)> EnumerateCandidates(int deviceCount)
        {
            var seen = new HashSet<int>();

            if (_options.PreferredDeviceIndex >= 0 &&
                _options.PreferredDeviceIndex < deviceCount &&
                seen.Add(_options.PreferredDeviceIndex))
            {
                yield return (
                    _options.PreferredDeviceIndex,
                    TryGetCaptureDeviceName(_options.PreferredDeviceIndex),
                    "requested-index",
                    false);
            }

            if (!string.IsNullOrWhiteSpace(_options.PreferredDeviceName))
            {
                for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
                {
                    var deviceName = TryGetCaptureDeviceName(deviceIndex);
                    if (string.Equals(deviceName, _options.PreferredDeviceName, StringComparison.OrdinalIgnoreCase) &&
                        seen.Add(deviceIndex))
                    {
                        yield return (deviceIndex, deviceName, "requested-name", false);
                    }
                }
            }

            for (var deviceIndex = 0; deviceIndex < deviceCount; deviceIndex++)
            {
                if (seen.Add(deviceIndex))
                {
                    yield return (deviceIndex, TryGetCaptureDeviceName(deviceIndex), "fallback", true);
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (_sync)
            {
                _rawAudio?.Write(e.Buffer, 0, e.BytesRecorded);
                if (_rawAudio is not null && _maxRecordingBytes > 0 && _rawAudio.Length >= _maxRecordingBytes)
                {
                    _logger.LogInformation("Maximum capture duration reached. Auto-stopping microphone capture.");
                    _waveIn?.StopRecording();
                }
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception is not null)
            {
                _recordingStopped.TrySetException(e.Exception);
            }
            else
            {
                _recordingStopped.TrySetResult(true);
            }
        }

        private static string TryGetCaptureDeviceName(int deviceIndex)
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
    }
}

internal static class AudioProcessing
{
    public static byte[] CreateSineWavePcm16Mono(int sampleRate, double durationSeconds, double frequencyHz)
    {
        var sampleCount = Math.Max(1, (int)(sampleRate * durationSeconds));
        var rawPcm = new byte[sampleCount * 2];

        for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            var t = sampleIndex / (double)sampleRate;
            var value = (short)(Math.Sin(t * Math.PI * 2 * frequencyHz) * short.MaxValue * 0.25);
            rawPcm[sampleIndex * 2] = (byte)(value & 0xFF);
            rawPcm[(sampleIndex * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return rawPcm;
    }

    public static byte[] EncodeWave(byte[] rawPcm, WaveFormat waveFormat)
    {
        using var outputStream = new MemoryStream();
        using (var writer = new WaveFileWriter(outputStream, waveFormat))
        {
            writer.Write(rawPcm, 0, rawPcm.Length);
        }

        return outputStream.ToArray();
    }

    public static AudioCaptureMetrics AnalyzeRawPcm16Mono(byte[] rawAudio, WaveFormat waveFormat)
    {
        if (rawAudio.Length < 2 || waveFormat.SampleRate <= 0 || waveFormat.BitsPerSample != 16)
        {
            return new AudioCaptureMetrics(0, 0, 0, 0, false);
        }

        var bytesPerSample = waveFormat.BitsPerSample / 8;
        var blockSize = bytesPerSample * Math.Max(1, waveFormat.Channels);
        if (blockSize <= 0 || rawAudio.Length < blockSize)
        {
            return new AudioCaptureMetrics(0, 0, 0, 0, false);
        }

        var sampleCount = rawAudio.Length / blockSize;
        if (sampleCount == 0)
        {
            return new AudioCaptureMetrics(0, 0, 0, 0, false);
        }

        const int activeThreshold = 512;
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
                if (sampleOffset + 1 >= rawAudio.Length)
                {
                    break;
                }

                var sample = (short)(rawAudio[sampleOffset] | (rawAudio[sampleOffset + 1] << 8));
                var abs = Math.Abs((int)sample);

                if (abs > 0)
                {
                    hasAnyNonZeroSample = true;
                }

                if (abs > peak)
                {
                    peak = abs;
                }

                if (abs >= activeThreshold)
                {
                    activeSamples++;
                }

                sumSquares += (long)sample * sample;
            }
        }

        var durationSeconds = sampleCount / (double)waveFormat.SampleRate;
        var channelSampleCount = Math.Max(1, sampleCount * waveFormat.Channels);
        var rms = Math.Sqrt(sumSquares / (double)channelSampleCount) / short.MaxValue;
        var peakNormalized = peak / (double)short.MaxValue;
        var activeRatio = activeSamples / (double)channelSampleCount;

        return new AudioCaptureMetrics(durationSeconds, rms, peakNormalized, activeRatio, hasAnyNonZeroSample);
    }
}
