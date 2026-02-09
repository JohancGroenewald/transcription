using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VoiceType;

public static class TaskbarPinManager
{
    public static bool TrySetCurrentExecutablePinned(bool pin, out string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            message = "Taskbar pinning is only supported on Windows.";
            return false;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            message = "Could not resolve the current executable path.";
            return false;
        }

        var folderPath = Path.GetDirectoryName(executablePath);
        var fileName = Path.GetFileName(executablePath);
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
        {
            message = "Invalid executable path.";
            return false;
        }

        object? shell = null;
        object? folder = null;
        object? item = null;
        object? verbs = null;
        object? targetVerb = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
            {
                message = "Shell automation is unavailable on this system.";
                return false;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                message = "Failed to initialize shell automation.";
                return false;
            }

            dynamic shellDynamic = shell;
            folder = shellDynamic.Namespace(folderPath);
            if (folder == null)
            {
                message = "Could not open executable folder in shell.";
                return false;
            }

            item = ((dynamic)folder).ParseName(fileName);
            if (item == null)
            {
                message = "Could not locate executable in shell namespace.";
                return false;
            }

            verbs = ((dynamic)item).Verbs();
            var count = (int)((dynamic)verbs).Count;

            var hasPinVerb = false;
            var hasUnpinVerb = false;

            for (var i = 0; i < count; i++)
            {
                var verb = ((dynamic)verbs).Item(i);
                if (verb == null)
                    continue;

                var verbName = (string?)verb.Name ?? string.Empty;
                var normalized = NormalizeVerbName(verbName);

                if (IsPinVerb(normalized))
                {
                    hasPinVerb = true;
                    if (pin && targetVerb == null)
                        targetVerb = verb;
                }
                else if (IsUnpinVerb(normalized))
                {
                    hasUnpinVerb = true;
                    if (!pin && targetVerb == null)
                        targetVerb = verb;
                }
            }

            if (targetVerb == null)
            {
                if (pin && hasUnpinVerb && !hasPinVerb)
                {
                    message = "VoiceType is already pinned to the taskbar.";
                    return true;
                }

                if (!pin && hasPinVerb && !hasUnpinVerb)
                {
                    message = "VoiceType is already unpinned from the taskbar.";
                    return true;
                }

                message = pin
                    ? "Pin command is not available. Windows may block programmatic pinning on this system."
                    : "Unpin command is not available. Windows may block programmatic unpinning on this system.";
                return false;
            }

            ((dynamic)targetVerb).DoIt();
            Thread.Sleep(250);

            message = pin
                ? "VoiceType pinned to taskbar."
                : "VoiceType unpinned from taskbar.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Taskbar operation failed: {ex.Message}";
            return false;
        }
        finally
        {
            ReleaseCom(targetVerb);
            ReleaseCom(verbs);
            ReleaseCom(item);
            ReleaseCom(folder);
            ReleaseCom(shell);
        }
    }

    private static string NormalizeVerbName(string name)
    {
        var cleaned = name.Replace("&", string.Empty).Replace("...", string.Empty);
        cleaned = Regex.Replace(cleaned, @"\s+", " ");
        return cleaned.Trim().ToLowerInvariant();
    }

    private static bool IsPinVerb(string normalized)
    {
        return normalized.Contains("taskbarpin", StringComparison.Ordinal)
            || normalized.Contains("pin to taskbar", StringComparison.Ordinal)
            || (normalized.Contains("taskbar", StringComparison.Ordinal) && normalized.Contains("pin", StringComparison.Ordinal) && !normalized.Contains("unpin", StringComparison.Ordinal));
    }

    private static bool IsUnpinVerb(string normalized)
    {
        return normalized.Contains("taskbarunpin", StringComparison.Ordinal)
            || normalized.Contains("unpin from taskbar", StringComparison.Ordinal)
            || (normalized.Contains("taskbar", StringComparison.Ordinal) && normalized.Contains("unpin", StringComparison.Ordinal));
    }

    private static void ReleaseCom(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
