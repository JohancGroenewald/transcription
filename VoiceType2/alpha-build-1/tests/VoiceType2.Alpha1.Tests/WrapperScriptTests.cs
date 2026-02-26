using Xunit;

namespace VoiceType2.Alpha1.Tests;

public sealed class WrapperScriptTests
{
    [Fact]
    public void All_ps1_wrappers_define_expected_base_command_shapes()
    {
        var scriptsRoot = GetScriptsDirectory();
        var cliScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-cli.ps1"));
        var tuiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-tui.ps1"));
        var allScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all.ps1"));
        var allTuiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all-tui.ps1"));
        var apiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-api.ps1"));

        Assert.Contains(@"src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj", cliScript);
        Assert.Contains(@"src/VoiceType2.App.Cli/VoiceType2.App.Cli.csproj", tuiScript);
        Assert.Contains(@"src/VoiceType2.ApiHost/VoiceType2.ApiHost.csproj", apiScript);

        Assert.Contains(@"""run""", cliScript);
        Assert.Contains(@"""tui""", tuiScript);
        Assert.Contains(@"""run""", allScript);
        Assert.Contains(@"""tui""", allTuiScript);
        Assert.Contains(@"""--project""", cliScript);
        Assert.Contains(@"""--project""", tuiScript);
        Assert.Contains(@"""--project""", apiScript);
        Assert.Contains(@"""--api-url""", cliScript);
        Assert.Contains(@"""--api-url""", tuiScript);
        Assert.Contains(@"""--mode""", allScript);
        Assert.Contains(@"""--mode""", allTuiScript);
        Assert.Contains(@"""managed""", allScript);
        Assert.Contains(@"""managed""", allTuiScript);
        Assert.Contains("dotnet @arguments", cliScript);
        Assert.Contains("dotnet @arguments", tuiScript);
        Assert.Contains("dotnet @cliArguments", allScript);
        Assert.Contains("dotnet @cliArguments", allTuiScript);
        Assert.Contains("dotnet @arguments", apiScript);
        Assert.Contains(@"Start-Process -FilePath ""dotnet""", allScript);
        Assert.Contains(@"Start-Process -FilePath ""dotnet""", allTuiScript);
        Assert.Contains(@"Stop-Process", allScript);
        Assert.Contains(@"Stop-Process", allTuiScript);
        Assert.Contains("[string]$RecordingDeviceId = \"\"", cliScript);
        Assert.Contains("[string]$RecordingDeviceId = \"\"", tuiScript);
        Assert.Contains("[string]$RecordingDeviceId = \"\"", allScript);
        Assert.Contains("[string]$RecordingDeviceId = \"\"", allTuiScript);
        Assert.Contains("[string]$PlaybackDeviceId = \"\"", cliScript);
        Assert.Contains("[string]$PlaybackDeviceId = \"\"", tuiScript);
        Assert.Contains("[string]$PlaybackDeviceId = \"\"", allScript);
        Assert.Contains("[string]$PlaybackDeviceId = \"\"", allTuiScript);
    }

    [Fact]
    public void Cli_and_all_tui_all_tui_ps1_wrappers_include_expected_optional_parameters()
    {
        var scriptsRoot = GetScriptsDirectory();
        var cliScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-cli.ps1"));
        var tuiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-tui.ps1"));
        var allScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all.ps1"));
        var allTuiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all-tui.ps1"));

        Assert.Contains(@"""--api-url""", cliScript);
        Assert.Contains(@"""--mode""", cliScript);
        Assert.Contains(@"""--session-mode""", cliScript);
        Assert.Contains(@"""--managed-start""", cliScript);
        Assert.Contains(@"""--api-url""", tuiScript);
        Assert.Contains(@"""--mode""", tuiScript);
        Assert.Contains(@"""--session-mode""", tuiScript);
        Assert.Contains(@"""--managed-start""", tuiScript);
        Assert.Contains(@"""--mode""", allScript);
        Assert.Contains(@"--api-timeout-ms", allScript);
        Assert.Contains(@"""--recording-device-id""", allScript);
        Assert.Contains(@"""--playback-device-id""", allScript);
        Assert.Contains(@"""--mode""", allTuiScript);
        Assert.Contains(@"""--urls""", allScript);
        Assert.Contains(@"""--urls""", allTuiScript);
        Assert.Contains(@"""--api-timeout-ms""", allTuiScript);
        Assert.Contains(@"""--recording-device-id""", allTuiScript);
        Assert.Contains(@"""--playback-device-id""", allTuiScript);
    }

    [Fact]
    public void All_cmd_wrappers_invoke_their_ps1_counterparts_with_execution_policy_bypass()
    {
        var scriptsRoot = GetScriptsDirectory();
        var cliCmd = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-cli.cmd"));
        var tuiCmd = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-tui.cmd"));
        var apiCmd = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-api.cmd"));
        var allCmd = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all.cmd"));
        var allTuiCmd = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all-tui.cmd"));
        var testCmd = File.ReadAllText(Path.Combine(scriptsRoot, "test-alpha1.cmd"));

        Assert.Contains("run-alpha1-cli.ps1", cliCmd);
        Assert.Contains("run-alpha1-tui.ps1", tuiCmd);
        Assert.Contains("run-alpha1-api.ps1", apiCmd);
        Assert.Contains("run-alpha1-all.ps1", allCmd);
        Assert.Contains("run-alpha1-all-tui.ps1", allTuiCmd);
        Assert.Contains("test-alpha1.ps1", testCmd);
        Assert.Contains("-NoProfile", cliCmd);
        Assert.Contains("-NoProfile", tuiCmd);
        Assert.Contains("-NoProfile", apiCmd);
        Assert.Contains("-NoProfile", allCmd);
        Assert.Contains("-NoProfile", allTuiCmd);
        Assert.Contains("-NoProfile", testCmd);
        Assert.Contains("-ExecutionPolicy Bypass", cliCmd);
        Assert.Contains("-ExecutionPolicy Bypass", tuiCmd);
        Assert.Contains("-ExecutionPolicy Bypass", apiCmd);
        Assert.Contains("-ExecutionPolicy Bypass", allCmd);
        Assert.Contains("-ExecutionPolicy Bypass", allTuiCmd);
        Assert.Contains("-ExecutionPolicy Bypass", testCmd);
    }

    [Fact]
    public void All_scripts_define_expected_defaults()
    {
        var scriptsRoot = GetScriptsDirectory();
        var cliScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-cli.ps1"));
        var tuiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-tui.ps1"));
        var allScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all.ps1"));
        var allTuiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-all-tui.ps1"));
        var testScript = File.ReadAllText(Path.Combine(scriptsRoot, "test-alpha1.ps1"));
        var apiScript = File.ReadAllText(Path.Combine(scriptsRoot, "run-alpha1-api.ps1"));

        Assert.Contains("[string]$ApiUrl = \"\"", cliScript);
        Assert.Contains("[string]$Mode = \"\"", cliScript);
        Assert.Contains("[string]$Mode = \"\"", tuiScript);
        Assert.Contains("[string]$ApiUrl = \"\"", allScript);
        Assert.Contains("[ValidateSet(\"Debug\", \"Release\")]", allScript);
        Assert.Contains("[int]$ApiTimeoutMs = 0", allScript);
        Assert.Contains("[int]$ApiTimeoutMs = 0", allTuiScript);
        Assert.Contains("[string]$ApiUrl = \"\"", allTuiScript);
        Assert.Contains("[ValidateSet(\"Debug\", \"Release\")]", allTuiScript);
        Assert.Contains("[ValidateSet(\"Debug\", \"Release\")]", testScript);
        Assert.Contains("[string]$ApiUrl = \"\"", apiScript);
        Assert.Contains("[string]$Mode = \"\"", apiScript);
    }

    private static string GetScriptsDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            var scriptsPath = Path.Combine(current.FullName, "scripts");
            var cliProjectPath = Path.Combine(current.FullName, "src", "VoiceType2.App.Cli");

            if (Directory.Exists(scriptsPath) && Directory.Exists(cliProjectPath))
            {
                return scriptsPath;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the scripts directory.");
    }
}
