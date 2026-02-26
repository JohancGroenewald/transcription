namespace VoiceType2.App.Cli;

internal sealed class AudioDeviceSelectionState(string? recordingDeviceId, string? playbackDeviceId)
{
    public string? RecordingDeviceId { get; set; } = recordingDeviceId;
    public string? PlaybackDeviceId { get; set; } = playbackDeviceId;
}

internal sealed record ParsedArguments(string Command, string[] PositionalArgs, Dictionary<string, string> Flags)
{
    public string? GetFlagValue(string key)
    {
        return Flags.TryGetValue(key, out var value) ? value : null;
    }
}
