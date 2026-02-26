using System.Reflection;
using Xunit;

namespace VoiceType2.Alpha1.Tests;

public sealed class CliStartupAndInteractionTests
{
    private const string UnreachableApiUrl = "http://127.0.0.1:59999";

    [Fact]
    public async Task RunAsync_returns_error_when_api_is_not_reachable_in_attach_mode()
    {
        var method = CliProgramTestHelpers.RunAsyncMethod;
        var context = CliProgramTestHelpers.CreateRunContext("dictate");
        object? managedApiConfig = null;
        var args = new object[] { UnreachableApiUrl, "attach", false, managedApiConfig, 250, 1000 };

        var result = await (Task<int>)method.Invoke(context, args)!;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_managed_start_is_disabled()
    {
        var method = CliProgramTestHelpers.RunAsyncMethod;
        var context = CliProgramTestHelpers.CreateRunContext("command");
        object? managedApiConfig = null;
        var args = new object[] { UnreachableApiUrl, "managed", false, managedApiConfig, 250, 1000 };

        var result = await (Task<int>)method.Invoke(context, args)!;

        Assert.Equal(1, result);
    }

    [Fact]
    public async Task TuiAsync_returns_error_when_managed_start_is_disabled()
    {
        var method = CliProgramTestHelpers.TuiAsyncMethod;
        var context = CliProgramTestHelpers.CreateRunContext("dictate");
        object? managedApiConfig = null;
        var args = new object[] { UnreachableApiUrl, "managed", false, managedApiConfig, 250, 1000 };

        var result = await (Task<int>)method.Invoke(context, args)!;

        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData("submit", "submit")]
    [InlineData("s", "submit")]
    [InlineData("cancel", "cancel")]
    [InlineData("c", "cancel")]
    [InlineData("retry", "retry")]
    [InlineData("r", "retry")]
    public void TryNormalizeAction_supports_known_inputs(string input, string expected)
    {
        var normalizeResult = CliProgramTestHelpers.TryNormalizeAction(input, out var normalized);

        Assert.True(normalizeResult);
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void TryNormalizeAction_rejects_unknown_input()
    {
        var normalizeResult = CliProgramTestHelpers.TryNormalizeAction("nonsense", out var normalized);

        Assert.False(normalizeResult);
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public async Task ResolveAsync_returns_error_when_action_is_missing_or_unsupported()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "ResolveAsync",
            parameterCount: 4,
            returnType: typeof(Task<int>));

        Assert.Equal(
            1,
            await (Task<int>)method.Invoke(null, [UnreachableApiUrl, "session-1", Array.Empty<string>(), (string?)null])!);

        Assert.Equal(
            1,
            await (Task<int>)method.Invoke(null, [UnreachableApiUrl, "session-1", new[] { "bad-action" }, (string?)null])!);
    }

    [Fact]
    public void PrintRunMenu_prints_all_repl_commands()
    {
        var method = CliProgramTestHelpers.FindMethod(
            CliProgramTestHelpers.ProgramType,
            "PrintRunMenu",
            parameterCount: 0);

        using var writer = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);
            method.Invoke(null, null);
            var output = writer.ToString();

            Assert.Contains("submit", output);
            Assert.Contains("cancel", output);
            Assert.Contains("retry", output);
            Assert.Contains("status", output);
            Assert.Contains("quit", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
