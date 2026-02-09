namespace VoiceType;

public static class Log
{
    private static int _enabled;

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VoiceType");

    private static readonly string LogPath = Path.Combine(LogDir, "voicetype.log");

    public static bool IsEnabled => Volatile.Read(ref _enabled) == 1;

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
}
