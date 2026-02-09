using System.Diagnostics;
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
        var productVersion = GetProductVersion();
        var assemblyVersion = _entryAssembly.GetName().Version?.ToString();

        var versionCandidates = new[] { informational, productVersion, assemblyVersion };
        foreach (var candidate in versionCandidates)
        {
            var cleaned = CleanVersion(candidate);
            if (!string.IsNullOrWhiteSpace(cleaned))
                return cleaned;
        }

        return "unknown";
    }

    private static string? GetProductVersion()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                return null;

            return FileVersionInfo.GetVersionInfo(processPath).ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    private static string? CleanVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Trim();

        var plusIndex = value.IndexOf('+');
        if (plusIndex >= 0)
            value = value[..plusIndex];

        var spaceIndex = value.IndexOf(' ');
        if (spaceIndex >= 0)
            value = value[..spaceIndex];

        return value.Trim();
    }
}
