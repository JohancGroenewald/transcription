using System.Text.Json;

namespace VoiceType2.Alpha2.ApiHost;

public sealed class RuntimeConfig
{
    public HostBindingConfig HostBinding { get; init; } = new();
    public RuntimeSecurityConfig RuntimeSecurity { get; init; } = new();
    public AudioCaptureConfig AudioCapture { get; init; } = new();
    public TranscriptionConfig Transcription { get; init; } = new();

    public static string DefaultConfigFile => "RuntimeConfig.sample.json";

    public static RuntimeConfig Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new RuntimeConfig();
        }

        var resolvedPath = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Runtime config file not found: {resolvedPath}");
        }

        var json = File.ReadAllText(resolvedPath);
        var loaded = JsonSerializer.Deserialize<RuntimeConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        }) ?? new RuntimeConfig();

        return loaded.Normalize();
    }

    public void Validate()
    {
        if (!Uri.IsWellFormedUriString(HostBinding.Urls, UriKind.Absolute))
        {
            throw new InvalidOperationException($"Invalid HostBinding.Urls '{HostBinding.Urls}'.");
        }

        if (!string.Equals(RuntimeSecurity.AuthMode, "none", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(RuntimeSecurity.AuthMode, "token-optional", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(RuntimeSecurity.AuthMode, "token-required", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported RuntimeSecurity.AuthMode '{RuntimeSecurity.AuthMode}'.");
        }

        if (!string.Equals(AudioCapture.Source, "fake", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(AudioCapture.Source, "wave-in", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported AudioCapture.Source '{AudioCapture.Source}'.");
        }

        if (!string.Equals(Transcription.Provider, "mock", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Transcription.Provider, "openai", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported Transcription.Provider '{Transcription.Provider}'.");
        }
    }

    public bool IsTokenAuthRequired =>
        string.Equals(RuntimeSecurity.AuthMode, "token-required", StringComparison.OrdinalIgnoreCase);

    public bool IsTokenAuthAllowed =>
        IsTokenAuthRequired ||
        string.Equals(RuntimeSecurity.AuthMode, "token-optional", StringComparison.OrdinalIgnoreCase);

    private RuntimeConfig Normalize()
    {
        HostBinding.Urls = string.IsNullOrWhiteSpace(HostBinding.Urls)
            ? "http://127.0.0.1:5250"
            : HostBinding.Urls;

        return this;
    }
}

public sealed class HostBindingConfig
{
    public string Urls { get; set; } = "http://127.0.0.1:5250";
}

public sealed class RuntimeSecurityConfig
{
    public string AuthMode { get; init; } = "token-optional";
}

public sealed class AudioCaptureConfig
{
    public string Source { get; init; } = "fake";
    public int PreferredDeviceIndex { get; init; } = -1;
    public string PreferredDeviceName { get; init; } = string.Empty;
    public int MaxDurationSeconds { get; init; } = 300;
}

public sealed class TranscriptionConfig
{
    public string Provider { get; init; } = "mock";
    public string Model { get; init; } = "whisper-1";
    public string Language { get; init; } = "en";
    public bool EnablePrompt { get; init; } = true;
    public string Prompt { get; init; } =
        "The speaker is always English. Transcribe the audio as technical instructions for a large language model.";
    public string OpenAiApiKeyEnvVar { get; init; } = "OPENAI_API_KEY";
}
