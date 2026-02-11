using System.Text.RegularExpressions;

namespace VoiceType;

public static class VoiceCommandParser
{
    public const string Exit = "exit";
    public const string Settings = "settings";
    public const string AutoSendYes = "auto_send_yes";
    public const string AutoSendNo = "auto_send_no";
    public const string Send = "send";
    public const string ShowVoiceCommands = "show_voice_commands";

    private static readonly Regex AutoSendEnableRegex = new(
        @"^(please |can you |could you )?(set |turn )?auto( ?send)?( to)? (yes|on|true|enable|enabled)( please)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AutoSendDisableRegex = new(
        @"^(please |can you |could you )?(set |turn )?auto( ?send)?( to)? (no|off|of|false|disable|disabled)( please)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Parse(
        string text,
        bool enableOpenSettingsVoiceCommand,
        bool enableExitAppVoiceCommand,
        bool enableToggleAutoEnterVoiceCommand,
        bool enableSendVoiceCommand,
        bool enableShowVoiceCommandsVoiceCommand)
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

        if (enableOpenSettingsVoiceCommand && MatchesPhrase(
            normalized,
            "open settings",
            "open settings screen",
            "show settings",
            "show settings screen"))
            return Settings;

        if (enableToggleAutoEnterVoiceCommand && MatchesAutoSendEnabled(normalized))
            return AutoSendYes;

        if (enableToggleAutoEnterVoiceCommand && MatchesAutoSendDisabled(normalized))
            return AutoSendNo;

        if (enableSendVoiceCommand && MatchesPhrase(
            normalized,
            "send",
            "send message",
            "send command",
            "submit",
            "press enter"))
            return Send;

        if (enableShowVoiceCommandsVoiceCommand && MatchesPhrase(
            normalized,
            "show voice commands",
            "show voice command",
            "list voice commands",
            "what are voice commands"))
            return ShowVoiceCommands;

        return null;
    }

    public static string GetDisplayName(string command)
    {
        return command switch
        {
            Exit => "Exit App",
            Settings => "Open Settings",
            AutoSendYes => "Auto-Send: Yes",
            AutoSendNo => "Auto-Send: No",
            Send => "Send (Press Enter)",
            ShowVoiceCommands => "Show Voice Commands",
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
                || normalized == "can you " + phrase
                || normalized == "could you " + phrase
                || normalized == phrase + " please")
                return true;
        }

        return false;
    }

    private static bool MatchesAutoSendEnabled(string normalized)
    {
        return AutoSendEnableRegex.IsMatch(normalized)
            || MatchesPhrase(
                normalized,
                "auto send yes",
                "autosend yes",
                "set auto send yes",
                "set autosend yes",
                "auto send on",
                "auto on",
                "autosend on",
                "set auto send on",
                "set autosend on",
                "enable auto send",
                "turn on auto send");
    }

    private static bool MatchesAutoSendDisabled(string normalized)
    {
        return AutoSendDisableRegex.IsMatch(normalized)
            || MatchesPhrase(
                normalized,
                "auto send no",
                "autosend no",
                "set auto send no",
                "set autosend no",
                "auto send off",
                "auto off",
                "autosend off",
                "set auto send off",
                "set autosend off",
                "disable auto send",
                "turn off auto send");
    }
}
