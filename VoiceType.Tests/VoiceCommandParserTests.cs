namespace VoiceType.Tests;

public class VoiceCommandParserTests
{
    [Theory]
    [InlineData("exit app")]
    [InlineData("Close VoiceType")]
    [InlineData("please close voicetype")]
    public void Parse_ReturnsExit_WhenExitPhraseMatches(string phrase)
    {
        var result = ParseAllEnabled(phrase);

        Assert.Equal(VoiceCommandParser.Exit, result);
    }

    [Theory]
    [InlineData("open settings")]
    [InlineData("show settings screen")]
    [InlineData("Could you open settings")]
    public void Parse_ReturnsSettings_WhenSettingsPhraseMatches(string phrase)
    {
        var result = ParseAllEnabled(phrase);

        Assert.Equal(VoiceCommandParser.Settings, result);
    }

    [Theory]
    [InlineData("auto-send on")]
    [InlineData("turn on auto send")]
    [InlineData("set auto send to true")]
    public void Parse_ReturnsAutoSendYes_WhenEnablePhraseMatches(string phrase)
    {
        var result = ParseAllEnabled(phrase);

        Assert.Equal(VoiceCommandParser.AutoSendYes, result);
    }

    [Theory]
    [InlineData("auto-send off")]
    [InlineData("auto off")]
    [InlineData("set auto send to false")]
    public void Parse_ReturnsAutoSendNo_WhenDisablePhraseMatches(string phrase)
    {
        var result = ParseAllEnabled(phrase);

        Assert.Equal(VoiceCommandParser.AutoSendNo, result);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("send command")]
    [InlineData("press enter")]
    public void Parse_ReturnsSend_WhenSubmitPhraseMatches(string phrase)
    {
        var result = ParseAllEnabled(phrase);

        Assert.Equal(VoiceCommandParser.Send, result);
    }

    [Theory]
    [InlineData("show voice commands")]
    [InlineData("list voice commands")]
    [InlineData("what are voice commands")]
    public void Parse_ReturnsShowVoiceCommands_WhenPhraseMatches(string phrase)
    {
        var result = ParseAllEnabled(phrase);

        Assert.Equal(VoiceCommandParser.ShowVoiceCommands, result);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenMatchingCommandIsDisabled()
    {
        var result = VoiceCommandParser.Parse(
            "exit app",
            enableOpenSettingsVoiceCommand: true,
            enableExitAppVoiceCommand: false,
            enableToggleAutoEnterVoiceCommand: true,
            enableSendVoiceCommand: true,
            enableShowVoiceCommandsVoiceCommand: true);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenPhraseIsUnknown()
    {
        var result = ParseAllEnabled("this is not a command");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("open settings")]
    [InlineData("show settings screen")]
    public void Parse_ReturnsNull_WhenSettingsCommandDisabled(string phrase)
    {
        var result = VoiceCommandParser.Parse(
            phrase,
            enableOpenSettingsVoiceCommand: false,
            enableExitAppVoiceCommand: true,
            enableToggleAutoEnterVoiceCommand: true,
            enableSendVoiceCommand: true,
            enableShowVoiceCommandsVoiceCommand: true);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("auto-send on")]
    [InlineData("auto-send off")]
    public void Parse_ReturnsNull_WhenAutoSendCommandDisabled(string phrase)
    {
        var result = VoiceCommandParser.Parse(
            phrase,
            enableOpenSettingsVoiceCommand: true,
            enableExitAppVoiceCommand: true,
            enableToggleAutoEnterVoiceCommand: false,
            enableSendVoiceCommand: true,
            enableShowVoiceCommandsVoiceCommand: true);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("submit")]
    [InlineData("send command")]
    public void Parse_ReturnsNull_WhenSubmitCommandDisabled(string phrase)
    {
        var result = VoiceCommandParser.Parse(
            phrase,
            enableOpenSettingsVoiceCommand: true,
            enableExitAppVoiceCommand: true,
            enableToggleAutoEnterVoiceCommand: true,
            enableSendVoiceCommand: false,
            enableShowVoiceCommandsVoiceCommand: true);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("show voice commands")]
    [InlineData("list voice commands")]
    public void Parse_ReturnsNull_WhenShowVoiceCommandsDisabled(string phrase)
    {
        var result = VoiceCommandParser.Parse(
            phrase,
            enableOpenSettingsVoiceCommand: true,
            enableExitAppVoiceCommand: true,
            enableToggleAutoEnterVoiceCommand: true,
            enableSendVoiceCommand: true,
            enableShowVoiceCommandsVoiceCommand: false);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_NormalizesPunctuationAndWhitespace()
    {
        var result = ParseAllEnabled("  SHOW...   voice   commands!!! ");

        Assert.Equal(VoiceCommandParser.ShowVoiceCommands, result);
    }

    [Theory]
    [InlineData(VoiceCommandParser.Exit, "Exit App")]
    [InlineData(VoiceCommandParser.Settings, "Open Settings")]
    [InlineData(VoiceCommandParser.AutoSendYes, "Auto-Send: Yes")]
    [InlineData(VoiceCommandParser.AutoSendNo, "Auto-Send: No")]
    [InlineData(VoiceCommandParser.Send, "Submit (Press Enter)")]
    [InlineData(VoiceCommandParser.ShowVoiceCommands, "Show Voice Commands")]
    public void GetDisplayName_ReturnsExpectedValues(string command, string expected)
    {
        var result = VoiceCommandParser.GetDisplayName(command);

        Assert.Equal(expected, result);
    }

    private static string? ParseAllEnabled(string phrase)
    {
        return VoiceCommandParser.Parse(
            phrase,
            enableOpenSettingsVoiceCommand: true,
            enableExitAppVoiceCommand: true,
            enableToggleAutoEnterVoiceCommand: true,
            enableSendVoiceCommand: true,
            enableShowVoiceCommandsVoiceCommand: true);
    }
}
