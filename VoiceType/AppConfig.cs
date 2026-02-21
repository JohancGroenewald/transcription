using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoiceType;

public class AppConfig
{
    private const string DefaultModel = "whisper-1";
    private const string DefaultPastedTextPrefix = "";
    public const int DefaultAudioDeviceIndex = -1;
    private const string DefaultTranscriptionPrompt =
        "The speaker is always English. Transcribe the audio as technical instructions for a large language model.";
    public const string DefaultPenHotkey = "F20";
    public const int DefaultOverlayDurationMs = 3000;
    public const int MinOverlayDurationMs = 500;
    public const int MaxOverlayDurationMs = 60000;
    public const int DefaultOverlayOpacityPercent = 10;
    public const int MinOverlayOpacityPercent = 0;
    public const int MaxOverlayOpacityPercent = 100;
    public const int DefaultOverlayWidthPercent = 62;
    public const int MinOverlayWidthPercent = 35;
    public const int MaxOverlayWidthPercent = 90;
    public const int DefaultOverlayFontSizePt = 13;
    public const int MinOverlayFontSizePt = 9;
    public const int MaxOverlayFontSizePt = 22;
    public const int DefaultOverlayFadeProfile = 2;
    public const int MinOverlayFadeProfile = 0;
    public const int MaxOverlayFadeProfile = 3;
    public const int OffOverlayFadeProfile = 0;
    public const int FastOverlayFadeProfile = 1;
    public const int BalancedOverlayFadeProfile = 2;
    public const int GentleOverlayFadeProfile = 3;
    public const int DefaultOverlayBackgroundMode = 1;
    public const int MinOverlayBackgroundMode = 0;
    public const int MaxOverlayBackgroundMode = 2;
    public const int OverlayBackgroundModeAlways = 0;
    public const int OverlayBackgroundModeHoverOnly = 1;
    public const int OverlayBackgroundModeNever = 2;
    public const int DefaultOverlayStackHorizontalOffsetPx = 0;
    public const int MinRemoteActionPopupLevel = 0;
    public const int DefaultRemoteActionPopupLevel = 1;
    public const int MaxRemoteActionPopupLevel = 2;
    public const bool DefaultRemoteListenWhileListening = true;
    public const bool DefaultRemoteListenWhilePreprocessing = true;
    public const bool DefaultRemoteListenWhileTextDisplayed = true;
    public const bool DefaultRemoteListenWhileCountdown = true;
    public const bool DefaultRemoteListenWhileIdle = true;
    public const bool DefaultRemoteSubmitWhileListening = true;
    public const bool DefaultRemoteSubmitWhilePreprocessing = false;
    public const bool DefaultRemoteSubmitWhileTextDisplayed = true;
    public const bool DefaultRemoteSubmitWhileCountdown = true;
    public const bool DefaultRemoteSubmitWhileIdle = false;
    public const bool DefaultRemoteActivateWhileListening = true;
    public const bool DefaultRemoteActivateWhilePreprocessing = true;
    public const bool DefaultRemoteActivateWhileTextDisplayed = true;
    public const bool DefaultRemoteActivateWhileCountdown = true;
    public const bool DefaultRemoteActivateWhileIdle = true;
    public const bool DefaultRemoteCloseWhileListening = true;
    public const bool DefaultRemoteCloseWhilePreprocessing = false;
    public const bool DefaultRemoteCloseWhileTextDisplayed = true;
    public const bool DefaultRemoteCloseWhileCountdown = true;
    public const bool DefaultRemoteCloseWhileIdle = false;
    public static readonly string[] OverlayFadeProfiles =
    [
        "Off",
        "Fast",
        "Balanced",
        "Gentle"
    ];
    public static readonly string[] OverlayBackgroundModes =
    [
        "Always",
        "On hover",
        "Never"
    ];
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
    public int OverlayOpacityPercent { get; set; } = DefaultOverlayOpacityPercent;
    public int OverlayWidthPercent { get; set; } = DefaultOverlayWidthPercent;
    public int OverlayFontSizePt { get; set; } = DefaultOverlayFontSizePt;
    public int OverlayFadeProfile { get; set; } = DefaultOverlayFadeProfile;
    public int OverlayBackgroundMode { get; set; } = DefaultOverlayBackgroundMode;
    public bool ShowOverlayBorder { get; set; } = true;
    public bool UseSimpleMicSpinner { get; set; }
    public bool EnablePreviewPlaybackCleanup { get; set; }
    public bool EnablePreviewPlayback { get; set; } = true;
    public bool EnablePenHotkey { get; set; }
    public string PenHotkey { get; set; } = DefaultPenHotkey;
    public bool EnableOpenSettingsVoiceCommand { get; set; } = true;
    public bool EnableExitAppVoiceCommand { get; set; } = true;
    public bool EnableToggleAutoEnterVoiceCommand { get; set; } = true;
    public bool EnableSendVoiceCommand { get; set; } = true;
    public bool EnableShowVoiceCommandsVoiceCommand { get; set; } = true;
    public int RemoteActionPopupLevel { get; set; } = DefaultRemoteActionPopupLevel;
    public bool EnableRemoteListenWhileListening { get; set; } = DefaultRemoteListenWhileListening;
    public bool EnableRemoteListenWhilePreprocessing { get; set; } = DefaultRemoteListenWhilePreprocessing;
    public bool EnableRemoteListenWhileTextDisplayed { get; set; } = DefaultRemoteListenWhileTextDisplayed;
    public bool EnableRemoteListenWhileCountdown { get; set; } = DefaultRemoteListenWhileCountdown;
    public bool EnableRemoteListenWhileIdle { get; set; } = DefaultRemoteListenWhileIdle;
    public bool EnableRemoteSubmitWhileListening { get; set; } = DefaultRemoteSubmitWhileListening;
    public bool EnableRemoteSubmitWhilePreprocessing { get; set; } = DefaultRemoteSubmitWhilePreprocessing;
    public bool EnableRemoteSubmitWhileTextDisplayed { get; set; } = DefaultRemoteSubmitWhileTextDisplayed;
    public bool EnableRemoteSubmitWhileCountdown { get; set; } = DefaultRemoteSubmitWhileCountdown;
    public bool EnableRemoteSubmitWhileIdle { get; set; } = DefaultRemoteSubmitWhileIdle;
    public bool EnableRemoteActivateWhileListening { get; set; } = DefaultRemoteActivateWhileListening;
    public bool EnableRemoteActivateWhilePreprocessing { get; set; } = DefaultRemoteActivateWhilePreprocessing;
    public bool EnableRemoteActivateWhileTextDisplayed { get; set; } = DefaultRemoteActivateWhileTextDisplayed;
    public bool EnableRemoteActivateWhileCountdown { get; set; } = DefaultRemoteActivateWhileCountdown;
    public bool EnableRemoteActivateWhileIdle { get; set; } = DefaultRemoteActivateWhileIdle;
    public bool EnableRemoteCloseWhileListening { get; set; } = DefaultRemoteCloseWhileListening;
    public bool EnableRemoteCloseWhilePreprocessing { get; set; } = DefaultRemoteCloseWhilePreprocessing;
    public bool EnableRemoteCloseWhileTextDisplayed { get; set; } = DefaultRemoteCloseWhileTextDisplayed;
    public bool EnableRemoteCloseWhileCountdown { get; set; } = DefaultRemoteCloseWhileCountdown;
    public bool EnableRemoteCloseWhileIdle { get; set; } = DefaultRemoteCloseWhileIdle;
    public bool EnablePastedTextPrefix { get; set; } = true;
    public string PastedTextPrefix { get; set; } = DefaultPastedTextPrefix;
    public bool EnableTranscriptionPrompt { get; set; } = true;
    public string TranscriptionPrompt { get; set; } = DefaultTranscriptionPrompt;
    public int MicrophoneInputDeviceIndex { get; set; } = DefaultAudioDeviceIndex;
    public string MicrophoneInputDeviceName { get; set; } = string.Empty;
    public int AudioOutputDeviceIndex { get; set; } = DefaultAudioDeviceIndex;
    public string AudioOutputDeviceName { get; set; } = string.Empty;
    public int OverlayStackHorizontalOffsetPx { get; set; } = DefaultOverlayStackHorizontalOffsetPx;

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceType");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static string DefaultConfigPath => ConfigPath;

    private class ConfigFile
    {
        public string? ProtectedApiKey { get; set; }
        public string? ApiKey { get; set; } // Legacy plaintext fallback
        public string Model { get; set; } = DefaultModel;
        public bool AutoEnter { get; set; }
        public bool EnableDebugLogging { get; set; }
        public bool EnableOverlayPopups { get; set; } = true;
        public int OverlayDurationMs { get; set; } = DefaultOverlayDurationMs;
        public int OverlayOpacityPercent { get; set; } = DefaultOverlayOpacityPercent;
        public int OverlayWidthPercent { get; set; } = DefaultOverlayWidthPercent;
        public int OverlayFontSizePt { get; set; } = DefaultOverlayFontSizePt;
        public int OverlayFadeProfile { get; set; } = DefaultOverlayFadeProfile;
        public int OverlayBackgroundMode { get; set; } = DefaultOverlayBackgroundMode;
        public bool ShowOverlayBorder { get; set; } = true;
        public bool UseSimpleMicSpinner { get; set; }
        public bool EnablePreviewPlaybackCleanup { get; set; }
        public bool EnablePreviewPlayback { get; set; } = true;
        public bool EnablePenHotkey { get; set; }
        public string PenHotkey { get; set; } = DefaultPenHotkey;
        public bool EnableOpenSettingsVoiceCommand { get; set; } = true;
        public bool EnableExitAppVoiceCommand { get; set; } = true;
        public bool EnableToggleAutoEnterVoiceCommand { get; set; } = true;
        public bool EnableSendVoiceCommand { get; set; } = true;
        public bool EnableShowVoiceCommandsVoiceCommand { get; set; } = true;
        public int RemoteActionPopupLevel { get; set; } = DefaultRemoteActionPopupLevel;
        public bool? EnableRemoteListenWhileListening { get; set; }
        public bool? EnableRemoteListenWhilePreprocessing { get; set; }
        public bool? EnableRemoteListenWhileTextDisplayed { get; set; }
        public bool? EnableRemoteListenWhileCountdown { get; set; }
        public bool? EnableRemoteListenWhileIdle { get; set; }
        public bool? EnableRemoteSubmitWhileListening { get; set; }
        public bool? EnableRemoteSubmitWhilePreprocessing { get; set; }
        public bool? EnableRemoteSubmitWhileTextDisplayed { get; set; }
        public bool? EnableRemoteSubmitWhileCountdown { get; set; }
        public bool? EnableRemoteSubmitWhileIdle { get; set; }
        public bool? EnableRemoteActivateWhileListening { get; set; }
        public bool? EnableRemoteActivateWhilePreprocessing { get; set; }
        public bool? EnableRemoteActivateWhileTextDisplayed { get; set; }
        public bool? EnableRemoteActivateWhileCountdown { get; set; }
        public bool? EnableRemoteActivateWhileIdle { get; set; }
        public bool? EnableRemoteCloseWhileListening { get; set; }
        public bool? EnableRemoteCloseWhilePreprocessing { get; set; }
        public bool? EnableRemoteCloseWhileTextDisplayed { get; set; }
        public bool? EnableRemoteCloseWhileCountdown { get; set; }
        public bool? EnableRemoteCloseWhileIdle { get; set; }
        public bool EnablePastedTextPrefix { get; set; } = true;
        public string PastedTextPrefix { get; set; } = DefaultPastedTextPrefix;
        public bool EnableTranscriptionPrompt { get; set; } = true;
        public string TranscriptionPrompt { get; set; } = DefaultTranscriptionPrompt;
        public int MicrophoneInputDeviceIndex { get; set; } = DefaultAudioDeviceIndex;
        public string MicrophoneInputDeviceName { get; set; } = string.Empty;
        public int AudioOutputDeviceIndex { get; set; } = DefaultAudioDeviceIndex;
        public string AudioOutputDeviceName { get; set; } = string.Empty;
        public int OverlayStackHorizontalOffsetPx { get; set; } = DefaultOverlayStackHorizontalOffsetPx;
    }

    public static AppConfig Load()
    {
        return Load(ConfigPath);
    }

    public static AppConfig Load(string configPath)
    {
        return LoadConfigFromPath(configPath, failSilently: true);
    }

    public static AppConfig LoadFromConfigPath(string configPath)
    {
        return LoadConfigFromPath(configPath, failSilently: false);
    }

    public void Save()
    {
        Save(ConfigPath);
    }

    public void Save(string configPath)
    {
        SaveToPath(configPath);
    }

    private static AppConfig LoadConfigFromPath(string configPath, bool failSilently)
    {
        var path = ResolveConfigPath(configPath);
        if (!File.Exists(path))
        {
            if (failSilently)
                return new AppConfig();

            throw new FileNotFoundException("Configuration file was not found.", path);
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            var configFile = JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions);
            if (configFile == null)
            {
                if (!failSilently)
                    throw new InvalidDataException("Configuration JSON was empty.");

                return new AppConfig();
            }

            return BuildFromConfigFile(configFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            if (failSilently)
            {
                Log.Error("Failed to load config. Using defaults.", ex);
                return new AppConfig();
            }

            throw;
        }
    }

    private static AppConfig BuildFromConfigFile(ConfigFile configFile)
    {
        return new AppConfig
        {
            ApiKey = LoadApiKey(configFile),
            Model = string.IsNullOrWhiteSpace(configFile.Model) ? DefaultModel : configFile.Model,
            AutoEnter = configFile.AutoEnter,
            EnableDebugLogging = configFile.EnableDebugLogging,
            EnableOverlayPopups = configFile.EnableOverlayPopups,
            OverlayDurationMs = NormalizeOverlayDuration(configFile.OverlayDurationMs),
            OverlayOpacityPercent = NormalizeOverlayOpacityPercent(configFile.OverlayOpacityPercent),
            OverlayWidthPercent = NormalizeOverlayWidthPercent(configFile.OverlayWidthPercent),
            OverlayFontSizePt = NormalizeOverlayFontSizePt(configFile.OverlayFontSizePt),
            OverlayFadeProfile = NormalizeOverlayFadeProfile(configFile.OverlayFadeProfile),
            OverlayBackgroundMode = NormalizeOverlayBackgroundMode(configFile.OverlayBackgroundMode),
            ShowOverlayBorder = configFile.ShowOverlayBorder,
            UseSimpleMicSpinner = configFile.UseSimpleMicSpinner,
            EnablePreviewPlaybackCleanup = configFile.EnablePreviewPlaybackCleanup,
            EnablePreviewPlayback = configFile.EnablePreviewPlayback,
            EnablePenHotkey = configFile.EnablePenHotkey,
            PenHotkey = NormalizePenHotkey(configFile.PenHotkey),
            EnableOpenSettingsVoiceCommand = configFile.EnableOpenSettingsVoiceCommand,
            EnableExitAppVoiceCommand = configFile.EnableExitAppVoiceCommand,
            EnableToggleAutoEnterVoiceCommand = configFile.EnableToggleAutoEnterVoiceCommand,
            EnableSendVoiceCommand = configFile.EnableSendVoiceCommand,
            EnableShowVoiceCommandsVoiceCommand = configFile.EnableShowVoiceCommandsVoiceCommand,
            RemoteActionPopupLevel = NormalizeRemoteActionPopupLevel(configFile.RemoteActionPopupLevel),
            EnableRemoteListenWhileListening = ReadRemoteStateFilterValue(
                configFile.EnableRemoteListenWhileListening,
                DefaultRemoteListenWhileListening),
            EnableRemoteListenWhilePreprocessing = ReadRemoteStateFilterValue(
                configFile.EnableRemoteListenWhilePreprocessing,
                DefaultRemoteListenWhilePreprocessing),
            EnableRemoteListenWhileTextDisplayed = ReadRemoteStateFilterValue(
                configFile.EnableRemoteListenWhileTextDisplayed,
                DefaultRemoteListenWhileTextDisplayed),
            EnableRemoteListenWhileCountdown = ReadRemoteStateFilterValue(
                configFile.EnableRemoteListenWhileCountdown,
                DefaultRemoteListenWhileCountdown),
            EnableRemoteListenWhileIdle = ReadRemoteStateFilterValue(
                configFile.EnableRemoteListenWhileIdle,
                DefaultRemoteListenWhileIdle),
            EnableRemoteSubmitWhileListening = ReadRemoteStateFilterValue(
                configFile.EnableRemoteSubmitWhileListening,
                DefaultRemoteSubmitWhileListening),
            EnableRemoteSubmitWhilePreprocessing = ReadRemoteStateFilterValue(
                configFile.EnableRemoteSubmitWhilePreprocessing,
                DefaultRemoteSubmitWhilePreprocessing),
            EnableRemoteSubmitWhileTextDisplayed = ReadRemoteStateFilterValue(
                configFile.EnableRemoteSubmitWhileTextDisplayed,
                DefaultRemoteSubmitWhileTextDisplayed),
            EnableRemoteSubmitWhileCountdown = ReadRemoteStateFilterValue(
                configFile.EnableRemoteSubmitWhileCountdown,
                DefaultRemoteSubmitWhileCountdown),
            EnableRemoteSubmitWhileIdle = ReadRemoteStateFilterValue(
                configFile.EnableRemoteSubmitWhileIdle,
                DefaultRemoteSubmitWhileIdle),
            EnableRemoteActivateWhileListening = ReadRemoteStateFilterValue(
                configFile.EnableRemoteActivateWhileListening,
                DefaultRemoteActivateWhileListening),
            EnableRemoteActivateWhilePreprocessing = ReadRemoteStateFilterValue(
                configFile.EnableRemoteActivateWhilePreprocessing,
                DefaultRemoteActivateWhilePreprocessing),
            EnableRemoteActivateWhileTextDisplayed = ReadRemoteStateFilterValue(
                configFile.EnableRemoteActivateWhileTextDisplayed,
                DefaultRemoteActivateWhileTextDisplayed),
            EnableRemoteActivateWhileCountdown = ReadRemoteStateFilterValue(
                configFile.EnableRemoteActivateWhileCountdown,
                DefaultRemoteActivateWhileCountdown),
            EnableRemoteActivateWhileIdle = ReadRemoteStateFilterValue(
                configFile.EnableRemoteActivateWhileIdle,
                DefaultRemoteActivateWhileIdle),
            EnableRemoteCloseWhileListening = ReadRemoteStateFilterValue(
                configFile.EnableRemoteCloseWhileListening,
                DefaultRemoteCloseWhileListening),
            EnableRemoteCloseWhilePreprocessing = ReadRemoteStateFilterValue(
                configFile.EnableRemoteCloseWhilePreprocessing,
                DefaultRemoteCloseWhilePreprocessing),
            EnableRemoteCloseWhileTextDisplayed = ReadRemoteStateFilterValue(
                configFile.EnableRemoteCloseWhileTextDisplayed,
                DefaultRemoteCloseWhileTextDisplayed),
            EnableRemoteCloseWhileCountdown = ReadRemoteStateFilterValue(
                configFile.EnableRemoteCloseWhileCountdown,
                DefaultRemoteCloseWhileCountdown),
            EnableRemoteCloseWhileIdle = ReadRemoteStateFilterValue(
                configFile.EnableRemoteCloseWhileIdle,
                DefaultRemoteCloseWhileIdle),
            EnablePastedTextPrefix = configFile.EnablePastedTextPrefix,
            PastedTextPrefix = configFile.PastedTextPrefix ?? DefaultPastedTextPrefix,
            EnableTranscriptionPrompt = configFile.EnableTranscriptionPrompt,
            TranscriptionPrompt = string.IsNullOrWhiteSpace(configFile.TranscriptionPrompt)
                ? DefaultTranscriptionPrompt
                : configFile.TranscriptionPrompt,
            MicrophoneInputDeviceIndex = NormalizeAudioDeviceIndex(configFile.MicrophoneInputDeviceIndex),
            MicrophoneInputDeviceName = configFile.MicrophoneInputDeviceName?.Trim() ?? string.Empty,
            AudioOutputDeviceIndex = NormalizeAudioDeviceIndex(configFile.AudioOutputDeviceIndex),
            AudioOutputDeviceName = configFile.AudioOutputDeviceName?.Trim() ?? string.Empty,
            OverlayStackHorizontalOffsetPx = configFile.OverlayStackHorizontalOffsetPx
        };
    }

    private static string ResolveConfigPath(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return ConfigPath;

        return Path.GetFullPath(configPath);
    }

    private void SaveToPath(string configPath)
    {
        try
        {
            var path = ResolveConfigPath(configPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ConfigDir);
            var configFile = new ConfigFile
            {
                ProtectedApiKey = ProtectApiKey(ApiKey),
                Model = string.IsNullOrWhiteSpace(Model) ? DefaultModel : Model,
                AutoEnter = AutoEnter,
                EnableDebugLogging = EnableDebugLogging,
                EnableOverlayPopups = EnableOverlayPopups,
                OverlayDurationMs = NormalizeOverlayDuration(OverlayDurationMs),
                OverlayOpacityPercent = NormalizeOverlayOpacityPercent(OverlayOpacityPercent),
                OverlayWidthPercent = NormalizeOverlayWidthPercent(OverlayWidthPercent),
                OverlayFontSizePt = NormalizeOverlayFontSizePt(OverlayFontSizePt),
                OverlayFadeProfile = NormalizeOverlayFadeProfile(OverlayFadeProfile),
                OverlayBackgroundMode = NormalizeOverlayBackgroundMode(OverlayBackgroundMode),
                ShowOverlayBorder = ShowOverlayBorder,
                UseSimpleMicSpinner = UseSimpleMicSpinner,
                EnablePreviewPlaybackCleanup = EnablePreviewPlaybackCleanup,
                EnablePreviewPlayback = EnablePreviewPlayback,
                EnablePenHotkey = EnablePenHotkey,
                PenHotkey = NormalizePenHotkey(PenHotkey),
                EnableOpenSettingsVoiceCommand = EnableOpenSettingsVoiceCommand,
                EnableExitAppVoiceCommand = EnableExitAppVoiceCommand,
                EnableToggleAutoEnterVoiceCommand = EnableToggleAutoEnterVoiceCommand,
                EnableSendVoiceCommand = EnableSendVoiceCommand,
                EnableShowVoiceCommandsVoiceCommand = EnableShowVoiceCommandsVoiceCommand,
                RemoteActionPopupLevel = NormalizeRemoteActionPopupLevel(RemoteActionPopupLevel),
                EnableRemoteListenWhileListening = EnableRemoteListenWhileListening,
                EnableRemoteListenWhilePreprocessing = EnableRemoteListenWhilePreprocessing,
                EnableRemoteListenWhileTextDisplayed = EnableRemoteListenWhileTextDisplayed,
                EnableRemoteListenWhileCountdown = EnableRemoteListenWhileCountdown,
                EnableRemoteListenWhileIdle = EnableRemoteListenWhileIdle,
                EnableRemoteSubmitWhileListening = EnableRemoteSubmitWhileListening,
                EnableRemoteSubmitWhilePreprocessing = EnableRemoteSubmitWhilePreprocessing,
                EnableRemoteSubmitWhileTextDisplayed = EnableRemoteSubmitWhileTextDisplayed,
                EnableRemoteSubmitWhileCountdown = EnableRemoteSubmitWhileCountdown,
                EnableRemoteSubmitWhileIdle = EnableRemoteSubmitWhileIdle,
                EnableRemoteActivateWhileListening = EnableRemoteActivateWhileListening,
                EnableRemoteActivateWhilePreprocessing = EnableRemoteActivateWhilePreprocessing,
                EnableRemoteActivateWhileTextDisplayed = EnableRemoteActivateWhileTextDisplayed,
                EnableRemoteActivateWhileCountdown = EnableRemoteActivateWhileCountdown,
                EnableRemoteActivateWhileIdle = EnableRemoteActivateWhileIdle,
                EnableRemoteCloseWhileListening = EnableRemoteCloseWhileListening,
                EnableRemoteCloseWhilePreprocessing = EnableRemoteCloseWhilePreprocessing,
                EnableRemoteCloseWhileTextDisplayed = EnableRemoteCloseWhileTextDisplayed,
                EnableRemoteCloseWhileCountdown = EnableRemoteCloseWhileCountdown,
                EnableRemoteCloseWhileIdle = EnableRemoteCloseWhileIdle,
                EnablePastedTextPrefix = EnablePastedTextPrefix,
                PastedTextPrefix = PastedTextPrefix ?? DefaultPastedTextPrefix,
                EnableTranscriptionPrompt = EnableTranscriptionPrompt,
                TranscriptionPrompt = string.IsNullOrWhiteSpace(TranscriptionPrompt)
                    ? DefaultTranscriptionPrompt
                    : TranscriptionPrompt,
                MicrophoneInputDeviceIndex = NormalizeAudioDeviceIndex(MicrophoneInputDeviceIndex),
                MicrophoneInputDeviceName = string.IsNullOrWhiteSpace(MicrophoneInputDeviceName)
                    ? string.Empty
                    : MicrophoneInputDeviceName.Trim(),
                AudioOutputDeviceIndex = NormalizeAudioDeviceIndex(AudioOutputDeviceIndex),
                AudioOutputDeviceName = string.IsNullOrWhiteSpace(AudioOutputDeviceName)
                    ? string.Empty
                    : AudioOutputDeviceName.Trim(),
                OverlayStackHorizontalOffsetPx = OverlayStackHorizontalOffsetPx
            };

            var json = JsonSerializer.Serialize(configFile, JsonOptions);
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, path, true);
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

    public static int NormalizeOverlayOpacityPercent(int opacityPercent)
    {
        if (opacityPercent < MinOverlayOpacityPercent)
            return MinOverlayOpacityPercent;
        if (opacityPercent > MaxOverlayOpacityPercent)
            return MaxOverlayOpacityPercent;
        return opacityPercent;
    }

    public static int NormalizeOverlayWidthPercent(int widthPercent)
    {
        if (widthPercent < MinOverlayWidthPercent)
            return MinOverlayWidthPercent;
        if (widthPercent > MaxOverlayWidthPercent)
            return MaxOverlayWidthPercent;
        return widthPercent;
    }

    public static int NormalizeOverlayFontSizePt(int fontSizePt)
    {
        if (fontSizePt < MinOverlayFontSizePt)
            return MinOverlayFontSizePt;
        if (fontSizePt > MaxOverlayFontSizePt)
            return MaxOverlayFontSizePt;
        return fontSizePt;
    }

    public static int NormalizeOverlayFadeProfile(int overlayFadeProfile)
    {
        if (overlayFadeProfile < MinOverlayFadeProfile)
            return MinOverlayFadeProfile;
        if (overlayFadeProfile > MaxOverlayFadeProfile)
            return MaxOverlayFadeProfile;
        return overlayFadeProfile;
    }

    public static int NormalizeOverlayBackgroundMode(int overlayBackgroundMode)
    {
        if (overlayBackgroundMode < MinOverlayBackgroundMode)
            return MinOverlayBackgroundMode;
        if (overlayBackgroundMode > MaxOverlayBackgroundMode)
            return MaxOverlayBackgroundMode;
        return overlayBackgroundMode;
    }

    public static int NormalizeRemoteActionPopupLevel(int level)
    {
        if (level < MinRemoteActionPopupLevel)
            return MinRemoteActionPopupLevel;
        if (level > MaxRemoteActionPopupLevel)
            return MaxRemoteActionPopupLevel;
        return level;
    }

    private static bool ReadRemoteStateFilterValue(bool? value, bool fallback)
    {
        return value ?? fallback;
    }

    public static int NormalizeAudioDeviceIndex(int deviceIndex)
    {
        return deviceIndex < DefaultAudioDeviceIndex ? DefaultAudioDeviceIndex : deviceIndex;
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
