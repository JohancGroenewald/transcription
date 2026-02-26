using System.Linq;
using VoiceType2.App.Cli;
using Xunit;

namespace VoiceType2.Alpha1.Tests;

public sealed class CliArgumentParsingTests
{
    private static readonly Type ProgramType = CliProgramTestHelpers.ProgramType;

    [Fact]
    public void ParseArguments_defaults_to_run_command()
    {
        var parsed = InvokeParseArguments(Array.Empty<string>());
        var flags = GetFlags(parsed);

        Assert.Equal("run", GetCommand(parsed));
        Assert.Empty(flags);
    }

    [Fact]
    public void ParseArguments_parses_flags_and_positionals()
    {
        var parsed = InvokeParseArguments(new[]
        {
            "status",
            "--api-url",
            "http://127.0.0.1:7000",
            "--session-id",
            "abc123",
            "extra"
        });

        Assert.Equal("status", GetCommand(parsed));
        Assert.Equal("http://127.0.0.1:7000", GetFlagValue(parsed, "--api-url"));
        Assert.Equal("abc123", GetFlagValue(parsed, "--session-id"));
        Assert.Equal(["extra"], GetPositional(parsed));
    }

    [Fact]
    public void ParseArguments_parses_equal_sign_notation()
    {
        var parsed = InvokeParseArguments(new[]
        {
            "resolve",
            "submit",
            "--session-id=abc123",
            "--api-url=http://127.0.0.1:7000"
        });

        Assert.Equal("resolve", GetCommand(parsed));
        Assert.Equal("submit", GetPositional(parsed)[0]);
        Assert.Equal("abc123", GetFlagValue(parsed, "--session-id"));
        Assert.Equal("http://127.0.0.1:7000", GetFlagValue(parsed, "--api-url"));
    }

    [Fact]
    public void ParseBool_parses_expected_inputs_and_defaults()
    {
        Assert.True(InvokeParseBool("true", false));
        Assert.True(InvokeParseBool("YES", false));
        Assert.True(InvokeParseBool("1", false));
        Assert.False(InvokeParseBool("0", true));
        Assert.False(InvokeParseBool("off", true));
        Assert.True(InvokeParseBool("maybe", true));
        Assert.False(InvokeParseBool("maybe", false));
    }

    [Fact]
    public void ParseInt_parses_positive_values_and_falls_back()
    {
        Assert.Equal(5000, InvokeParseInt("5000", 1000));
        Assert.Equal(1000, InvokeParseInt("0", 1000));
        Assert.Equal(1000, InvokeParseInt("-1", 1000));
        Assert.Equal(1000, InvokeParseInt("abc", 1000));
    }

    private static object InvokeParseArguments(string[] args)
    {
        var method = CliProgramTestHelpers.FindMethod(
            ProgramType,
            "ParseArguments",
            parameterCount: 1);
        return method.Invoke(null, [args])!;
    }

    private static bool InvokeParseBool(string value, bool defaultValue)
    {
        var method = CliProgramTestHelpers.FindMethod(
            ProgramType,
            "ParseBool",
            parameterCount: 2,
            returnType: typeof(bool));
        return (bool)(method.Invoke(null, [value, defaultValue])!);
    }

    private static int InvokeParseInt(string value, int defaultValue)
    {
        var method = CliProgramTestHelpers.FindMethod(
            ProgramType,
            "ParseInt",
            parameterCount: 2,
            returnType: typeof(int));
        return (int)(method.Invoke(null, [value, defaultValue])!);
    }

    private static string GetCommand(object parsed)
    {
        var commandProperty = parsed.GetType().GetProperty("Command");
        return (string)commandProperty!.GetValue(parsed)!;
    }

    private static string[] GetPositional(object parsed)
    {
        var positionalProperty = parsed.GetType().GetProperty("PositionalArgs");
        return (string[])positionalProperty!.GetValue(parsed)!;
    }

    private static string GetFlagValue(object parsed, string key)
    {
        return GetFlags(parsed)[key];
    }

    private static Dictionary<string, string> GetFlags(object parsed)
    {
        var flagsProperty = parsed.GetType().GetProperty("Flags");
        return (Dictionary<string, string>)flagsProperty!.GetValue(parsed)!;
    }
}
