using System.Reflection;
using System.Linq;
using VoiceType2.App.Cli;

namespace VoiceType2.Alpha1.Tests;

internal static class CliProgramTestHelpers
{
    private const BindingFlags StaticMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Type _runContextType = ResolveRunContextType();

    internal static readonly Type ProgramType = ResolveProgramType();

    internal static MethodInfo FindMethod(
        Type type,
        string containsName,
        int parameterCount,
        Type? returnType = null,
        bool requireInstance = false)
    {
        var methods = type.GetMethods(requireInstance ? InstanceMethods : StaticMethods)
            .Where(method =>
                method.Name.Contains(containsName, StringComparison.Ordinal)
                && method.GetParameters().Length == parameterCount
                && (returnType is null || method.ReturnType == returnType));

        return methods.Single();
    }

    internal static object CreateRunContext(string sessionMode = "dictate")
    {
        var context = Activator.CreateInstance(_runContextType, true)
            ?? throw new InvalidOperationException("Failed to create run context for CLI instance methods.");

        var field = _runContextType.GetField("sessionMode", InstanceMethods);
        if (field is not null)
        {
            field.SetValue(context, sessionMode);
        }

        return context;
    }

    internal static MethodInfo RunAsyncMethod
    {
        get
        {
            return FindMethod(
                _runContextType,
                "RunAsync",
                parameterCount: 8,
                returnType: typeof(Task<int>),
                requireInstance: true);
        }
    }

    internal static MethodInfo TuiAsyncMethod
    {
        get
        {
            return FindMethod(
                _runContextType,
                "TuiAsync",
                parameterCount: 8,
                returnType: typeof(Task<int>),
                requireInstance: true);
        }
    }

    internal static bool TryNormalizeAction(string action, out string normalized)
    {
        var method = FindMethod(
            ProgramType,
            "TryNormalizeAction",
            parameterCount: 2,
            returnType: typeof(bool));

        var args = new object[] { action, string.Empty };
        var normalizedResult = (bool)(method.Invoke(null, args) ?? false);
        normalized = (string)args[1];
        return normalizedResult;
    }

    private static Type ResolveProgramType()
    {
        var assembly = typeof(ClientConfigLoader).Assembly;

        return assembly.GetType("VoiceType2.App.Cli.Program")
            ?? assembly.GetTypes().Single(type => type.Name == "Program");
    }

    private static Type ResolveRunContextType()
    {
        var programType = ResolveProgramType();

        return programType.GetNestedTypes(StaticMethods | InstanceMethods)
            .First(type => type.GetMethods(InstanceMethods)
                .Any(method => method.Name.Contains("RunAsync", StringComparison.Ordinal)));
    }
}
