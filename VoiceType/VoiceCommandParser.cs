using System.Text.RegularExpressions;

namespace VoiceType;

public static class VoiceCommandParser
{
    public const string Exit = "exit";
    public const string Settings = "settings";
    public const string EnableAutoEnter = "enable_auto_enter";
    public const string DisableAutoEnter = "disable_auto_enter";

    public static string? Parse(
        string text,
        bool enableOpenSettingsVoiceCommand,
        bool enableExitAppVoiceCommand,
        bool enableToggleAutoEnterVoiceCommand)
    {
        var normalized = NormalizeCommandText(text);
        if (string.IsNullOrEmpty(normalized))
            return null;

        if (enableExitAppVoiceCommand && MatchesPhrase(
            normalized,
            "exit app",
            "close app",
            "quit app",
            "close voice type",
            "exit voice type",
            "close voicetype",
            "exit voicetype"))
            return Exit;

        if (enableOpenSettingsVoiceCommand && MatchesPhrase(normalized, "open settings"))
            return Settings;

        if (enableToggleAutoEnterVoiceCommand && MatchesPhrase(
            normalized,
            "enable auto enter",
            "enable autoenter",
            "turn on auto enter",
            "turn on autoenter",
            "auto enter on",
            "autoenter on"))
            return EnableAutoEnter;

        if (enableToggleAutoEnterVoiceCommand && MatchesPhrase(
            normalized,
            "disable auto enter",
            "disable autoenter",
            "turn off auto enter",
            "turn off autoenter",
            "auto enter off",
            "autoenter off"))
            return DisableAutoEnter;

        return null;
    }

    public static string GetDisplayName(string command)
    {
        return command switch
        {
            Exit => "Exit App",
            Settings => "Open Settings",
            EnableAutoEnter => "Enable Auto-Enter",
            DisableAutoEnter => "Disable Auto-Enter",
            _ => command
        };
    }

    private static string NormalizeCommandText(string text)
    {
        var normalized = Regex.Replace(text.ToLowerInvariant(), @"[^a-z0-9]+", " ");
        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static bool MatchesPhrase(string normalized, params string[] phrases)
    {
        foreach (var phrase in phrases)
        {
            if (normalized == phrase
                || normalized == "please " + phrase
                || normalized == phrase + " please")
                return true;
        }

        return false;
    }
}
