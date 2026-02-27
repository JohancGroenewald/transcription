using System.Text.Json;
using System.Text.Json.Nodes;

namespace VoiceType2.App.Cli;

internal static class ClientConfigLoader
{
    public const string DefaultConfigFile = "ClientConfig.json";
    public const string SampleConfigFile = "ClientConfig.sample.json";

    private const int SearchDepth = 8;

    public static ClientConfig Load(string? path)
    {
        var resolvedPath = ResolveConfigPath(path);
        var loadedJson = ParseConfigJson(resolvedPath);
        var samplePath = FindConfigFilePath(SampleConfigFile);
        if (string.IsNullOrWhiteSpace(samplePath))
        {
            throw new FileNotFoundException(
                $"Could not locate '{SampleConfigFile}' while loading client config.");
        }

        var sampleJson = ParseConfigJson(samplePath);
        var mergedJson = MergeConfigs(sampleJson, loadedJson);
        var config = DeserializeConfig(mergedJson);

        if (string.IsNullOrWhiteSpace(config.ApiUrl))
        {
            throw new InvalidOperationException("ClientConfig.ApiUrl must be configured.");
        }

        if (string.IsNullOrWhiteSpace(config.Mode))
        {
            throw new InvalidOperationException("ClientConfig.Mode must be configured.");
        }

        if (string.IsNullOrWhiteSpace(config.SessionMode))
        {
            throw new InvalidOperationException("ClientConfig.SessionMode must be configured.");
        }

        if (config.ApiTimeoutMs <= 0)
        {
            throw new InvalidOperationException("ClientConfig.ApiTimeoutMs must be greater than zero.");
        }

        if (config.ShutdownTimeoutMs <= 0)
        {
            throw new InvalidOperationException("ClientConfig.ShutdownTimeoutMs must be greater than zero.");
        }

        return config;
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
                throw new FileNotFoundException($"Client config file not found: {resolvedConfiguredPath}");
            }

            return resolvedConfiguredPath;
        }

        var searchPaths = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var searchPath in searchPaths)
        {
            var current = searchPath;
            for (var depth = 0; depth < SearchDepth && !string.IsNullOrWhiteSpace(current); depth++)
            {
                var currentConfigPath = Path.Combine(current, DefaultConfigFile);
                if (File.Exists(currentConfigPath))
                {
                    return currentConfigPath;
                }

                var currentSamplePath = Path.Combine(current, SampleConfigFile);
                if (File.Exists(currentSamplePath))
                {
                    var generatedPath = Path.Combine(current, DefaultConfigFile);
                    if (!File.Exists(generatedPath))
                    {
                        File.Copy(currentSamplePath, generatedPath, overwrite: false);
                    }

                    return generatedPath;
                }

                current = Directory.GetParent(current)?.FullName;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate a client config. Expected '{DefaultConfigFile}' or '{SampleConfigFile}' in the current or parent directories.");
    }

    private static string? FindConfigFilePath(string fileName)
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

    private static JsonObject ParseConfigJson(string path)
    {
        var raw = File.ReadAllText(path);
        var node = JsonNode.Parse(raw);
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException($"Invalid client config format in {path}. Expected a JSON object.");
        }

        return root;
    }

    private static ClientConfig DeserializeConfig(JsonObject mergedJson)
    {
        return mergedJson.Deserialize<ClientConfig>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
               ?? new ClientConfig();
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

public sealed class ClientConfig
{
    public string ApiUrl { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string SessionMode { get; init; } = string.Empty;
    public bool ManagedStart { get; init; }
    public int ApiTimeoutMs { get; init; }
    public int ShutdownTimeoutMs { get; init; }
    public string? DefaultRecordingDeviceId { get; init; }
    public string? DefaultPlaybackDeviceId { get; init; }
}
