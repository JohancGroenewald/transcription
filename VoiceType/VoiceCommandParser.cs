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

        if (enableOpenSettingsVoiceCommand && MatchesPhrase(normalized, "open settings"))
            return Settings;

        if (enableToggleAutoEnterVoiceCommand && MatchesPhrase(
            normalized,
            "auto send yes",
            "autosend yes",
            "set auto send yes",
            "set autosend yes"))
            return AutoSendYes;

        if (enableToggleAutoEnterVoiceCommand && MatchesPhrase(
            normalized,
            "auto send no",
            "autosend no",
            "set auto send no",
            "set autosend no"))
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
                || normalized == phrase + " please")
                return true;
        }

        return false;
    }
}
