using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using NAudio.Wave;
using VoiceType2.Core.Contracts;

namespace VoiceType2.ApiHost.Services;

public interface IHostAudioCaptureSession : IDisposable
{
    Task<Stream> GetAudioStreamAsync(CancellationToken cancellationToken);
}

public interface IHostAudioBootstrapper
{
    Task<IHostAudioCaptureSession?> InitializeRecordingCaptureAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken);

    Task InitializePlaybackAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken);

    Task PlayConfirmationToneAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class HostAudioBootstrapper : IHostAudioBootstrapper
{
    private const int ConfirmationDurationMs = 120;
    private const int ConfirmationFrequencyHz = 880;
    private const int DefaultBitDepth = 16;
    private const int DefaultChannels = 1;
    private const int DefaultSampleRate = 16_000;
    private const int DefaultToneAmplitude = short.MaxValue / 5;

    private static readonly TimeSpan DefaultCaptureWindow = TimeSpan.FromMilliseconds(350);

    public Task<IHostAudioCaptureSession?> InitializeRecordingCaptureAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _ = sessionId;
        _ = correlationId;
        _ = cancellationToken;

        if (audioDevices is null || string.IsNullOrWhiteSpace(audioDevices.RecordingDeviceId))
        {
            return Task.FromResult<IHostAudioCaptureSession?>(null);
        }

        if (!TryParseDeviceIndex(audioDevices.RecordingDeviceId, "rec", out var recordingDeviceIndex))
        {
            return Task.FromResult<IHostAudioCaptureSession?>(null);
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IHostAudioCaptureSession?>(null);
        }

        if (!HasCaptureDevice(recordingDeviceIndex))
        {
            return Task.FromResult<IHostAudioCaptureSession?>(null);
        }

        try
        {
            return Task.FromResult<IHostAudioCaptureSession?>(new WindowsRecordingCaptureSession(
                recordingDeviceIndex,
                DefaultCaptureWindow));
        }
        catch
        {
            return Task.FromResult<IHostAudioCaptureSession?>(null);
        }
    }

    public Task InitializePlaybackAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _ = sessionId;
        _ = correlationId;
        _ = cancellationToken;

        if (audioDevices is null || string.IsNullOrWhiteSpace(audioDevices.PlaybackDeviceId))
        {
            return Task.CompletedTask;
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        if (!TryParseDeviceIndex(audioDevices.PlaybackDeviceId, "play", out var playbackDeviceIndex))
        {
            return Task.CompletedTask;
        }

        if (!HasPlaybackDevice(playbackDeviceIndex))
        {
            return Task.CompletedTask;
        }

        try
        {
            using var waveOut = new WaveOutEvent
            {
                DeviceNumber = playbackDeviceIndex
            };
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    public Task PlayConfirmationToneAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken)
    {
        _ = sessionId;
        _ = correlationId;

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        if (audioDevices is null || string.IsNullOrWhiteSpace(audioDevices.PlaybackDeviceId))
        {
            return Task.CompletedTask;
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.CompletedTask;
        }

        if (!TryParseDeviceIndex(audioDevices.PlaybackDeviceId, "play", out var playbackDeviceIndex))
        {
            return Task.CompletedTask;
        }

        if (!HasPlaybackDevice(playbackDeviceIndex))
        {
            return Task.CompletedTask;
        }

        return PlayToneAsync(playbackDeviceIndex, cancellationToken);
    }

    private static async Task PlayToneAsync(int playbackDeviceIndex, CancellationToken cancellationToken)
    {
        var toneBytes = BuildConfirmationToneWave();
        if (toneBytes.Length == 0)
        {
            return;
        }

        await using var toneStream = new MemoryStream(toneBytes);
        try
        {
            using var waveOut = new WaveOutEvent
            {
                DeviceNumber = playbackDeviceIndex,
                DesiredLatency = 100
            };
            using var reader = new WaveFileReader(toneStream);
            waveOut.Init(reader);
            waveOut.Play();

            while (waveOut.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(15, cancellationToken);
            }
        }
        catch
        {
        }
    }

    private static byte[] BuildConfirmationToneWave()
    {
        var waveFormat = new WaveFormat(DefaultSampleRate, DefaultBitDepth, DefaultChannels);
        using var stream = new MemoryStream();

        try
        {
            using var writer = new WaveFileWriter(stream, waveFormat);
            var sampleCount = (int)(DefaultSampleRate * (ConfirmationDurationMs / 1000.0));
            var twoPi = 2.0 * Math.PI;
            for (var sample = 0; sample < sampleCount; sample++)
            {
                var t = sample / (double)DefaultSampleRate;
                var normalized = Math.Sin(twoPi * ConfirmationFrequencyHz * t);
                var signedSample = (short)(normalized * DefaultToneAmplitude);
                var bytes = new byte[2];
                BinaryPrimitives.WriteInt16LittleEndian(bytes, signedSample);
                writer.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            return Array.Empty<byte>();
        }

        return stream.ToArray();
    }

    private static bool TryParseDeviceIndex(string deviceId, string expectedPrefix, out int deviceIndex)
    {
        deviceIndex = 0;
        var prefix = $"{expectedPrefix}:";
        if (!deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rawIndex = deviceId[prefix.Length..];
        return int.TryParse(rawIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out deviceIndex);
    }

    private static bool HasCaptureDevice(int index)
    {
        return IsValidDeviceIndex(index, "NAudio.Wave.WaveIn");
    }

    private static bool HasPlaybackDevice(int index)
    {
        return IsValidDeviceIndex(index, "NAudio.Wave.WaveOut");
    }

    private static bool IsValidDeviceIndex(int index, string typeName)
    {
        if (index < 0)
        {
            return false;
        }

        try
        {
            var type = Type.GetType($"{typeName}, NAudio");
            if (type is null)
            {
                return false;
            }

            var deviceCountProperty = type.GetProperty(
                "DeviceCount",
                BindingFlags.Public | BindingFlags.Static);
            if (deviceCountProperty is null)
            {
                return false;
            }

            if (deviceCountProperty.GetValue(null) is not int deviceCount)
            {
                return false;
            }

            return index < deviceCount;
        }
        catch
        {
            return false;
        }
    }

    private sealed class WindowsRecordingCaptureSession : IHostAudioCaptureSession
    {
        private readonly MemoryStream _recordedAudio;
        private readonly WaveInEvent _waveIn;
        private readonly WaveFileWriter _waveWriter;
        private readonly object _sync = new();
        private readonly CancellationTokenSource _captureCancellation = new();
        private readonly Task _captureWindowTask;
        private bool _disposed;
        private bool _finalized;

        public WindowsRecordingCaptureSession(int deviceIndex, TimeSpan captureWindow)
        {
            _recordedAudio = new MemoryStream();
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(DefaultSampleRate, DefaultBitDepth, DefaultChannels),
                BufferMilliseconds = 100
            };

            _waveWriter = new WaveFileWriter(_recordedAudio, _waveIn.WaveFormat);
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();

            _captureWindowTask = CaptureWindowAsync(captureWindow, _captureCancellation.Token);
        }

        public async Task<Stream> GetAudioStreamAsync(CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _captureCancellation.Token);
            try
            {
                await _captureWindowTask.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                StopAndFinalizeRecording();
            }

            lock (_sync)
            {
                return new MemoryStream(_recordedAudio.ToArray(), 0, (int)_recordedAudio.Length, false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _captureCancellation.Cancel();
            StopAndFinalizeRecording();
            _captureCancellation.Dispose();
            _waveIn.Dispose();
        }

        private async Task CaptureWindowAsync(TimeSpan captureWindow, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(captureWindow, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                StopAndFinalizeRecording();
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
            }
        }

        private void StopAndFinalizeRecording()
        {
            lock (_sync)
            {
                if (_finalized)
                {
                    return;
                }

                _finalized = true;

                try
                {
                    _waveIn.StopRecording();
                }
                catch
                {
                }

                try
                {
                    _waveWriter.Flush();
                    _waveWriter.Dispose();
                }
                catch
                {
                }
            }
        }
    }
}
