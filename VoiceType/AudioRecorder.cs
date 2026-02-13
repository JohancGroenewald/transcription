using NAudio.Wave;

namespace VoiceType;

public readonly record struct AudioCaptureMetrics(
    TimeSpan Duration,
    double Rms,
    double Peak,
    double ActiveSampleRatio)
{
    // Conservative silence gate to avoid transcribing pure noise/silence.
    public bool IsLikelySilence =>
        Duration < TimeSpan.FromMilliseconds(250)
        || (Rms < 0.0025 && Peak < 0.012 && ActiveSampleRatio < 0.01);
}

public class AudioRecorder : IDisposable
{
    private static readonly TimeSpan MaxRecordingDuration = TimeSpan.FromMinutes(5);
    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private WaveFormat? _waveFormat;
    private TaskCompletionSource<bool>? _recordingStopped;
    private bool _disposed;
    private bool _maxRecordingLimitReached;
    private long _maxRecordingBytes;

    public AudioCaptureMetrics LastCaptureMetrics { get; private set; }
    public event Action<int>? InputLevelChanged;

    public void Start()
    {
        ThrowIfDisposed();
        if (_waveIn != null)
            throw new InvalidOperationException("Already recording.");

        LastCaptureMetrics = default;
        _audioBuffer = new MemoryStream();
        _recordingStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _maxRecordingLimitReached = false;
        _maxRecordingBytes = 0;

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono - ideal for speech
        };
        _waveIn = waveIn;
        _waveFormat = waveIn.WaveFormat;
        _maxRecordingBytes = (long)_waveFormat.AverageBytesPerSecond * (long)MaxRecordingDuration.TotalSeconds;

        waveIn.DataAvailable += OnDataAvailable;
        waveIn.RecordingStopped += OnRecordingStopped;

        try
        {
            waveIn.StartRecording();
        }
        catch
        {
            CleanupRecorder();
            throw;
        }
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
        if (stopped != null)
        {
            if (!stopped.Task.Wait(TimeSpan.FromSeconds(2)))
                Log.Error("Timed out waiting for microphone capture to stop.");
            else
                stopped.Task.GetAwaiter().GetResult();
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
            if (_audioBuffer == null)
                return;

            _audioBuffer?.Write(e.Buffer, 0, e.BytesRecorded);

            if (!_maxRecordingLimitReached &&
                _maxRecordingBytes > 0 &&
                _audioBuffer.Length >= _maxRecordingBytes)
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

        var sampleCount = rawAudio.Length / 2;
        if (sampleCount == 0)
            return default;

        const int activeThreshold = 512; // ~1.6% of full-scale

        long sumSquares = 0;
        var peak = 0;
        var activeSamples = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = i * 2;
            var sample = (short)(rawAudio[offset] | (rawAudio[offset + 1] << 8));
            var sampleValue = (int)sample;
            var abs = Math.Abs(sampleValue);

            if (abs > peak)
                peak = abs;
            if (abs >= activeThreshold)
                activeSamples++;

            sumSquares += (long)sampleValue * sampleValue;
        }

        var duration = TimeSpan.FromSeconds(sampleCount / (double)waveFormat.SampleRate);
        var rms = Math.Sqrt(sumSquares / (double)sampleCount) / short.MaxValue;
        var peakNormalized = peak / (double)short.MaxValue;
        var activeRatio = activeSamples / (double)sampleCount;

        return new AudioCaptureMetrics(duration, rms, peakNormalized, activeRatio);
    }

    private static int CalculateInputLevelPercent(byte[] pcm16Buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2)
            return 0;

        var sampleCount = bytesRecorded / 2;
        if (sampleCount == 0)
            return 0;

        long sumSquares = 0;
        var peak = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var offset = i * 2;
            var sample = (short)(pcm16Buffer[offset] | (pcm16Buffer[offset + 1] << 8));
            var sampleValue = (int)sample;
            var abs = Math.Abs(sampleValue);

            if (abs > peak)
                peak = abs;

            sumSquares += (long)sampleValue * sampleValue;
        }

        var rms = Math.Sqrt(sumSquares / (double)sampleCount) / short.MaxValue;
        var peakNormalized = peak / (double)short.MaxValue;

        // Blend peak and RMS to get a responsive but stable meter.
        var blended = Math.Max(peakNormalized, rms * 2.8);
        var curved = Math.Pow(Math.Clamp(blended, 0, 1), 0.55);
        return (int)Math.Round(curved * 100);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioRecorder));
    }
}
