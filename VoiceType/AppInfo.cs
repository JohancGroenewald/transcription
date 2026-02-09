using System.Reflection;

namespace VoiceType;

public static class AppInfo
{
    private static readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private static readonly Assembly _entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

    public static string Version { get; } = ResolveVersion();

    public static DateTimeOffset StartedAtLocal => _startedAtUtc.ToLocalTime();

    public static TimeSpan Uptime => DateTimeOffset.UtcNow - _startedAtUtc;

    public static void Initialize()
    {
        _ = Version;
        _ = _startedAtUtc;
    }

    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime < TimeSpan.Zero)
            uptime = TimeSpan.Zero;

        var totalHours = (int)uptime.TotalHours;
        return $"{totalHours:00}:{uptime.Minutes:00}:{uptime.Seconds:00}";
    }

    private static string ResolveVersion()
    {
        var informational = _entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Trim();

        var assemblyVersion = _entryAssembly.GetName().Version?.ToString();
        if (!string.IsNullOrWhiteSpace(assemblyVersion))
            return assemblyVersion;

        return "unknown";
    }
}
