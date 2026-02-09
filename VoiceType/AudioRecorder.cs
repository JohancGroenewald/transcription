using NAudio.Wave;

namespace VoiceType;

public class AudioRecorder : IDisposable
{
    private readonly object _sync = new();
    private WaveInEvent? _waveIn;
    private MemoryStream? _audioBuffer;
    private WaveFormat? _waveFormat;
    private TaskCompletionSource<bool>? _recordingStopped;
    private bool _disposed;

    public void Start()
    {
        ThrowIfDisposed();
        if (_waveIn != null)
            throw new InvalidOperationException("Already recording.");

        _audioBuffer = new MemoryStream();
        _recordingStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1) // 16kHz, 16-bit, mono - ideal for speech
        };
        _waveIn = waveIn;
        _waveFormat = waveIn.WaveFormat;

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
        lock (_sync)
        {
            _audioBuffer?.Write(e.Buffer, 0, e.BytesRecorded);
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
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AudioRecorder));
    }
}
