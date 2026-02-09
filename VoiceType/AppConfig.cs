using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceType;

public class AppConfig
{
    private const string DefaultModel = "whisper-1";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public bool AutoEnter { get; set; }
    public bool EnableDebugLogging { get; set; }
    public bool EnableOpenSettingsVoiceCommand { get; set; } = true;
    public bool EnableExitAppVoiceCommand { get; set; } = true;

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceType");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private class ConfigFile
    {
        public string? ProtectedApiKey { get; set; }
        public string? ApiKey { get; set; } // Legacy plaintext fallback
        public string Model { get; set; } = DefaultModel;
        public bool AutoEnter { get; set; }
        public bool EnableDebugLogging { get; set; }
        public bool EnableOpenSettingsVoiceCommand { get; set; } = true;
        public bool EnableExitAppVoiceCommand { get; set; } = true;
    }

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var configFile = JsonSerializer.Deserialize<ConfigFile>(json);
            if (configFile == null)
                return new AppConfig();

            return new AppConfig
            {
                ApiKey = LoadApiKey(configFile),
                Model = string.IsNullOrWhiteSpace(configFile.Model) ? DefaultModel : configFile.Model,
                AutoEnter = configFile.AutoEnter,
                EnableDebugLogging = configFile.EnableDebugLogging,
                EnableOpenSettingsVoiceCommand = configFile.EnableOpenSettingsVoiceCommand,
                EnableExitAppVoiceCommand = configFile.EnableExitAppVoiceCommand
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Error("Failed to load config. Using defaults.", ex);
            return new AppConfig();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var configFile = new ConfigFile
            {
                ProtectedApiKey = ProtectApiKey(ApiKey),
                Model = string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model,
                AutoEnter = AutoEnter,
                EnableDebugLogging = EnableDebugLogging,
                EnableOpenSettingsVoiceCommand = EnableOpenSettingsVoiceCommand,
                EnableExitAppVoiceCommand = EnableExitAppVoiceCommand
            };

            var json = JsonSerializer.Serialize(configFile, JsonOptions);
            var tempPath = ConfigPath + ".tmp";
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, ConfigPath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            Log.Error("Failed to save config.", ex);
            throw;
        }
    }

    private static string LoadApiKey(ConfigFile configFile)
    {
        if (!string.IsNullOrWhiteSpace(configFile.ProtectedApiKey))
        {
            try
            {
                return UnprotectApiKey(configFile.ProtectedApiKey);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                Log.Error("Failed to decrypt API key from config. Falling back to legacy key.", ex);
            }
        }

        return configFile.ApiKey?.Trim() ?? string.Empty;
    }

    private static string? ProtectApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var plainBytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string UnprotectApiKey(string protectedApiKey)
    {
        var protectedBytes = Convert.FromBase64String(protectedApiKey);
        var plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes).Trim();
    }
}
