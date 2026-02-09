using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceType;

public class AppConfig
{
    private const string DefaultModel = "whisper-1";
    public const string DefaultPenHotkey = "F20";
    public const int DefaultOverlayDurationMs = 3000;
    public const int MinOverlayDurationMs = 500;
    public const int MaxOverlayDurationMs = 60000;
    private static readonly string[] SupportedPenHotkeys =
    [
        "F13",
        "F14",
        "F15",
        "F16",
        "F17",
        "F18",
        "F19",
        "F20",
        "F21",
        "F22",
        "F23",
        "F24",
        "LaunchApp1",
        "LaunchApp2"
    ];

    private static readonly Dictionary<string, int> PenHotkeyVirtualKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F13"] = 0x7C,
        ["F14"] = 0x7D,
        ["F15"] = 0x7E,
        ["F16"] = 0x7F,
        ["F17"] = 0x80,
        ["F18"] = 0x81,
        ["F19"] = 0x82,
        ["F20"] = 0x83,
        ["F21"] = 0x84,
        ["F22"] = 0x85,
        ["F23"] = 0x86,
        ["F24"] = 0x87,
        ["LaunchApp1"] = 0xB6,
        ["LaunchApp2"] = 0xB7
    };

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = DefaultModel;
    public bool AutoEnter { get; set; }
    public bool EnableDebugLogging { get; set; }
    public bool EnableOverlayPopups { get; set; } = true;
    public int OverlayDurationMs { get; set; } = DefaultOverlayDurationMs;
    public bool EnablePenHotkey { get; set; }
    public string PenHotkey { get; set; } = DefaultPenHotkey;
    public bool EnableOpenSettingsVoiceCommand { get; set; } = true;
    public bool EnableExitAppVoiceCommand { get; set; } = true;
    public bool EnableToggleAutoEnterVoiceCommand { get; set; } = true;

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
        public bool EnableOverlayPopups { get; set; } = true;
        public int OverlayDurationMs { get; set; } = DefaultOverlayDurationMs;
        public bool EnablePenHotkey { get; set; }
        public string PenHotkey { get; set; } = DefaultPenHotkey;
        public bool EnableOpenSettingsVoiceCommand { get; set; } = true;
        public bool EnableExitAppVoiceCommand { get; set; } = true;
        public bool EnableToggleAutoEnterVoiceCommand { get; set; } = true;
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
                EnableOverlayPopups = configFile.EnableOverlayPopups,
                OverlayDurationMs = NormalizeOverlayDuration(configFile.OverlayDurationMs),
                EnablePenHotkey = configFile.EnablePenHotkey,
                PenHotkey = NormalizePenHotkey(configFile.PenHotkey),
                EnableOpenSettingsVoiceCommand = configFile.EnableOpenSettingsVoiceCommand,
                EnableExitAppVoiceCommand = configFile.EnableExitAppVoiceCommand,
                EnableToggleAutoEnterVoiceCommand = configFile.EnableToggleAutoEnterVoiceCommand
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
                EnableOverlayPopups = EnableOverlayPopups,
                OverlayDurationMs = NormalizeOverlayDuration(OverlayDurationMs),
                EnablePenHotkey = EnablePenHotkey,
                PenHotkey = NormalizePenHotkey(PenHotkey),
                EnableOpenSettingsVoiceCommand = EnableOpenSettingsVoiceCommand,
                EnableExitAppVoiceCommand = EnableExitAppVoiceCommand,
                EnableToggleAutoEnterVoiceCommand = EnableToggleAutoEnterVoiceCommand
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

    public static int NormalizeOverlayDuration(int durationMs)
    {
        if (durationMs < MinOverlayDurationMs)
            return MinOverlayDurationMs;
        if (durationMs > MaxOverlayDurationMs)
            return MaxOverlayDurationMs;
        return durationMs;
    }

    public static IReadOnlyList<string> GetSupportedPenHotkeys()
    {
        return SupportedPenHotkeys;
    }

    public static string NormalizePenHotkey(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
            return DefaultPenHotkey;

        foreach (var candidate in SupportedPenHotkeys)
        {
            if (string.Equals(candidate, hotkey.Trim(), StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return DefaultPenHotkey;
    }

    public static bool TryGetVirtualKeyForPenHotkey(string? hotkey, out int vk)
    {
        var normalized = NormalizePenHotkey(hotkey);
        return PenHotkeyVirtualKeys.TryGetValue(normalized, out vk);
    }
}
