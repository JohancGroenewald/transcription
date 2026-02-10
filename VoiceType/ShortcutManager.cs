using System.Runtime.InteropServices;

namespace VoiceType;

public static class ShortcutManager
{
    public static bool TryCreateCurrentExecutableShortcut(
        string shortcutFileName,
        string arguments,
        string description,
        out string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            message = "Shortcut creation is only supported on Windows.";
            return false;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            message = "Could not resolve the current executable path.";
            return false;
        }

        var folderPath = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            message = "Could not resolve the executable directory.";
            return false;
        }

        var shortcutPath = Path.Combine(folderPath, shortcutFileName);
        object? shell = null;
        dynamic? shortcut = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                message = "WScript.Shell automation is unavailable on this system.";
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                message = "Failed to initialize WScript.Shell automation.";
                return false;
            }

            dynamic shellDynamic = shell;
            shortcut = shellDynamic.CreateShortcut(shortcutPath);
            if (shortcut == null)
            {
                message = "Failed to create shortcut object.";
                return false;
            }

            shortcut.TargetPath = executablePath;
            shortcut.Arguments = arguments;
            shortcut.WorkingDirectory = folderPath;
            shortcut.IconLocation = $"{executablePath},0";
            shortcut.Description = description;
            shortcut.Save();

            message = $"Shortcut created: {shortcutPath}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Shortcut creation failed: {ex.Message}";
            return false;
        }
        finally
        {
            ReleaseCom(shortcut);
            ReleaseCom(shell);
        }
    }

    private static void ReleaseCom(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
