using System.Runtime.InteropServices;
using System.Text;

namespace VoiceType;

public static class TextInjector
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Returns true if a suitable window is focused to receive pasted text.
    /// </summary>
    public static bool HasPasteTarget()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        if (fg == GetDesktopWindow()) return false;
        if (fg == GetShellWindow()) return false;

        // Check for common "no target" windows
        var sb = new StringBuilder(256);
        GetClassName(fg, sb, 256);
        var className = sb.ToString();

        // Progman / WorkerW = desktop wallpaper, Shell_TrayWnd = taskbar
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return false;

        return true;
    }

    /// <summary>
    /// Puts text on the clipboard and simulates Ctrl+V to paste it
    /// into the currently focused application.
    /// Returns true if pasted, false if only copied to clipboard.
    /// </summary>
    public static bool InjectText(string text, bool autoEnter = false)
    {
        Clipboard.SetText(text);

        if (!HasPasteTarget())
        {
            Log.Info("No paste target detected, text copied to clipboard only");
            return false;
        }

        // Small delay to let the clipboard settle
        Thread.Sleep(50);

        // Press Ctrl+V
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

        if (autoEnter)
        {
            Thread.Sleep(30);
            keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        return true;
    }
}
