using System.Text.Json;

namespace VoiceType2.ApiHost;

public sealed class RuntimeConfig
{
    public HostBindingConfig HostBinding { get; init; } = new();
    public SessionPolicyConfig SessionPolicy { get; init; } = new();
    public RuntimeSecurityConfig RuntimeSecurity { get; init; } = new();
    public TranscriptionDefaultsConfig TranscriptionDefaults { get; init; } = new();

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
        if (string.IsNullOrWhiteSpace(HostBinding.Urls))
        {
            throw new InvalidOperationException("HostBinding.Urls is required.");
        }

        foreach (var url in HostBinding.Urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                throw new InvalidOperationException($"Invalid host URL: {url}");
            }
        }

        if (SessionPolicy.MaxConcurrentSessions < 1)
        {
            throw new InvalidOperationException("SessionPolicy.MaxConcurrentSessions must be greater than zero.");
        }

        if (SessionPolicy.DefaultSessionTimeoutMs < 0)
        {
            throw new InvalidOperationException("SessionPolicy.DefaultSessionTimeoutMs must be zero or greater.");
        }

        if (SessionPolicy.SessionIdleTimeoutMs < 0)
        {
            throw new InvalidOperationException("SessionPolicy.SessionIdleTimeoutMs must be zero or greater.");
        }
    }

    private RuntimeConfig Normalize()
    {
        HostBinding.Urls = string.IsNullOrWhiteSpace(HostBinding.Urls) ? "http://127.0.0.1:5240" : HostBinding.Urls;
        return this;
    }
}

public sealed class HostBindingConfig
{
    public string Urls { get; set; } = "http://127.0.0.1:5240";
    public bool UseHttps { get; init; }
}

public sealed class SessionPolicyConfig
{
    public int MaxConcurrentSessions { get; init; } = 4;
    public int DefaultSessionTimeoutMs { get; init; } = 300000;
    public int SessionIdleTimeoutMs { get; init; } = 120000;
}

public sealed class RuntimeSecurityConfig
{
    public string AuthMode { get; init; } = "token-optional";
    public bool EnableCorrelationIds { get; init; } = true;
    public bool StructuredErrorEnvelope { get; init; } = true;
}

public sealed class TranscriptionDefaultsConfig
{
    public string Provider { get; init; } = "mock";
    public string DefaultLanguage { get; init; } = "en-US";
    public string DefaultPrompt { get; init; } = string.Empty;
    public int DefaultTimeoutMs { get; init; } = 120000;
}

