using System.Globalization;
using NAudio.Wave;
using VoiceType2.Core.Contracts;

namespace VoiceType2.ApiHost.Services;

public interface IHostAudioBootstrapper
{
    Task<IDisposable?> InitializeRecordingCaptureAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken);

    Task InitializePlaybackAsync(
        AudioDeviceSelection? audioDevices,
        string sessionId,
        string correlationId,
        CancellationToken cancellationToken);
}

public sealed class HostAudioBootstrapper : IHostAudioBootstrapper
{
    public Task<IDisposable?> InitializeRecordingCaptureAsync(
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
            return Task.FromResult<IDisposable?>(null);
        }

        if (!TryParseDeviceIndex(audioDevices.RecordingDeviceId, "rec", out var recordingDeviceIndex))
        {
            return Task.FromResult<IDisposable?>(null);
        }

        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IDisposable?>(null);
        }

        try
        {
            return Task.FromResult<IDisposable?>(new WindowsRecordingCaptureLease(recordingDeviceIndex));
        }
        catch
        {
            return Task.FromResult<IDisposable?>(null);
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

    private sealed class WindowsRecordingCaptureLease : IDisposable
    {
        private readonly WaveInEvent _waveIn;

        public WindowsRecordingCaptureLease(int deviceIndex)
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100
            };

            _waveIn.StartRecording();
        }

        public void Dispose()
        {
            try
            {
                _waveIn.StopRecording();
            }
            catch
            {
            }

            _waveIn.Dispose();
        }
    }
}
