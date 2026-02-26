using System.Text.Json.Nodes;
using System.Text.Json;

namespace VoiceType2.ApiHost;

public sealed class RuntimeConfig
{
    public static string DefaultConfigFile => "RuntimeConfig.json";
    public static string SampleConfigFile => "RuntimeConfig.sample.json";

    private const int SearchDepth = 8;

    public HostBindingConfig HostBinding { get; init; } = new();
    public SessionPolicyConfig SessionPolicy { get; init; } = new();
    public RuntimeSecurityConfig RuntimeSecurity { get; init; } = new();
    public TranscriptionDefaultsConfig TranscriptionDefaults { get; init; } = new();

    public static RuntimeConfig Load(string? path)
    {
        var resolvedPath = ResolveConfigPath(path);
        var hasExplicitPath = !string.IsNullOrWhiteSpace(path);
        var samplePath = FindFile(SampleConfigFile);

        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Runtime config file not found: {resolvedPath}");
        }

        var loadedJson = ParseConfigJson(resolvedPath);
        if (!hasExplicitPath && string.IsNullOrWhiteSpace(samplePath))
        {
            throw new FileNotFoundException(
                $"Could not locate '{SampleConfigFile}' while loading runtime config.");
        }

        if (string.IsNullOrWhiteSpace(samplePath))
        {
            return (loadedJson.Deserialize<RuntimeConfig>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            }) ?? new RuntimeConfig()).Normalize();
        }

        var sampleJson = ParseConfigJson(samplePath);
        var mergedJson = MergeConfigs(sampleJson, loadedJson);

        var loaded = mergedJson.Deserialize<RuntimeConfig>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        }) ?? new RuntimeConfig();

        return loaded.Normalize();
    }

    private static string ResolveConfigPath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolvedConfiguredPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath);

            if (!File.Exists(resolvedConfiguredPath))
            {
                throw new FileNotFoundException($"Runtime config file not found: {resolvedConfiguredPath}");
            }

            return resolvedConfiguredPath;
        }

        var existingConfig = FindFile(DefaultConfigFile);
        if (!string.IsNullOrWhiteSpace(existingConfig))
        {
            return existingConfig;
        }

        var samplePath = FindFile(SampleConfigFile);
        if (string.IsNullOrWhiteSpace(samplePath))
        {
            throw new FileNotFoundException(
                $"Could not locate a runtime config. Expected '{DefaultConfigFile}' or '{SampleConfigFile}' in the current or parent directories.");
        }

        var generatedPath = Path.Combine(Path.GetDirectoryName(samplePath) ?? string.Empty, DefaultConfigFile);
        if (!File.Exists(generatedPath))
        {
            File.Copy(samplePath, generatedPath, overwrite: false);
        }

        return generatedPath;
    }

    private static string? FindFile(string fileName)
    {
        var roots = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var root in roots)
        {
            var current = root;
            for (var depth = 0; depth < SearchDepth && !string.IsNullOrWhiteSpace(current); depth++)
            {
                var candidate = Path.GetFullPath(Path.Combine(current, fileName));
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        return null;
    }

    public void Validate()
    {
        if (!IsSupportedAuthMode(RuntimeSecurity.AuthMode))
        {
            throw new InvalidOperationException(
                $"Unsupported RuntimeSecurity.AuthMode '{RuntimeSecurity.AuthMode}'. " +
                "Supported values are: none, token-optional, token-required.");
        }

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

    public bool IsTokenAuthRequired =>
        string.Equals(RuntimeSecurity.AuthMode, "token-required", StringComparison.OrdinalIgnoreCase);

    public bool IsTokenAuthOptional =>
        string.Equals(RuntimeSecurity.AuthMode, "token-optional", StringComparison.OrdinalIgnoreCase);

    public bool IsTokenAuthAllowed =>
        IsTokenAuthRequired || IsTokenAuthOptional;

    public static bool IsSupportedAuthMode(string? authMode)
    {
        return string.Equals(authMode, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authMode, "token-optional", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authMode, "token-required", StringComparison.OrdinalIgnoreCase);
    }

    private RuntimeConfig Normalize()
    {
        return this;
    }

    private static JsonObject ParseConfigJson(string path)
    {
        var raw = File.ReadAllText(path);
        var node = JsonNode.Parse(raw);
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException($"Invalid runtime config format in {path}. Expected a JSON object.");
        }

        return root;
    }

    private static JsonObject MergeConfigs(JsonObject baseConfig, JsonObject overlay)
    {
        var result = (JsonObject)baseConfig.DeepClone();

        foreach (var property in overlay)
        {
            if (!result.ContainsKey(property.Key))
            {
                result[property.Key] = property.Value?.DeepClone();
                continue;
            }

            if (result[property.Key] is JsonObject resultObject && property.Value is JsonObject overlayObject)
            {
                result[property.Key] = MergeConfigs(resultObject, overlayObject);
                continue;
            }

            result[property.Key] = property.Value?.DeepClone();
        }

        return result;
    }
}

public sealed class HostBindingConfig
{
    public string Urls { get; set; } = string.Empty;
    public bool UseHttps { get; init; }
}

public sealed class SessionPolicyConfig
{
    public int MaxConcurrentSessions { get; init; }
    public int DefaultSessionTimeoutMs { get; init; }
    public int SessionIdleTimeoutMs { get; init; }
}

public sealed class RuntimeSecurityConfig
{
    public string AuthMode { get; init; } = string.Empty;
    public bool EnableCorrelationIds { get; init; }
    public bool StructuredErrorEnvelope { get; init; }
}

public sealed class TranscriptionDefaultsConfig
{
    public string Provider { get; init; } = string.Empty;
    public string DefaultLanguage { get; init; } = string.Empty;
    public string DefaultPrompt { get; init; } = string.Empty;
    public int DefaultTimeoutMs { get; init; }
}
