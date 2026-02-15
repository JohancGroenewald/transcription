namespace VoiceType;

public static class Log
{
    private static int _enabled;
    private static int _rolledOnStartup;

    private const int DefaultMaxArchivedLogs = 10;

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceType");

    private static readonly string LogPath = Path.Combine(LogDir, "voicetype.log");

    public static bool IsEnabled => Volatile.Read(ref _enabled) == 1;

    /// <summary>
    /// Rolls (archives) the current log file so each app run starts with a fresh log.
    /// Best-effort only; never throws.
    /// </summary>
    public static void RollOnStartup(int maxArchivedLogs = DefaultMaxArchivedLogs)
    {
        if (Interlocked.Exchange(ref _rolledOnStartup, 1) == 1)
            return;

        try
        {
            Directory.CreateDirectory(LogDir);

            if (!File.Exists(LogPath))
                return;

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var pid = SafeGet(() => System.Diagnostics.Process.GetCurrentProcess().Id, 0);
            var baseName = Path.GetFileNameWithoutExtension(LogPath);
            var ext = Path.GetExtension(LogPath);

            string? archivePath = null;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var suffix = attempt == 0 ? string.Empty : $".{attempt}";
                var candidate = Path.Combine(LogDir, $"{baseName}.{timestamp}.pid{pid}{suffix}{ext}");
                if (File.Exists(candidate))
                    continue;
                archivePath = candidate;
                break;
            }

            if (archivePath == null)
                return;

            File.Move(LogPath, archivePath);

            if (maxArchivedLogs <= 0)
                return;

            // Keep the newest N archived logs; delete the rest.
            var archivedLogs = Directory
                .EnumerateFiles(LogDir, $"{baseName}.*{ext}", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ToList();

            for (var i = maxArchivedLogs; i < archivedLogs.Count; i++)
            {
                try
                {
                    archivedLogs[i].Delete();
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
        catch
        {
            // Don't throw from logging
        }
    }

    public static void Configure(bool enabled)
    {
        Interlocked.Exchange(ref _enabled, enabled ? 1 : 0);
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Error(string message, Exception ex)
    {
        Write("ERROR", $"{message}: {ex}");
    }

    private static void Write(string level, string message)
    {
        if (!IsEnabled)
            return;

        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Don't throw from logging
        }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }
}
