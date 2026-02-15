using System.Runtime.InteropServices;
using System.Text;

namespace VoiceType;

public static class TextInjector
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const byte VK_RETURN = 0x0D;
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int ClipboardSettleDelayMs = 80;
    private const int PostPasteBeforeEnterDelayMs = 110;
    private const int TargetRetryAttempts = 4;
    private const int TargetRetryDelayMs = 35;
    private const int WM_GETTEXTLENGTH = 0x000E;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    /// <summary>
    /// Returns true if a suitable window is focused to receive pasted text.
    /// </summary>
    public static bool HasPasteTarget()
    {
        return TryGetSuitableTargetWindow(TargetRetryAttempts, TargetRetryDelayMs) != IntPtr.Zero;
    }

    /// <summary>
    /// Returns true when the active suitable target appears to already contain text.
    /// </summary>
    public static bool TargetHasExistingText()
    {
        var target = TryGetSuitableTargetWindow(TargetRetryAttempts, TargetRetryDelayMs);
        return DoesWindowHaveTextInTextInputTarget(target);
    }

    /// <summary>
    /// Puts text on the clipboard and simulates Ctrl+V to paste it
    /// into the currently focused application.
    /// Returns true if pasted, false if only copied to clipboard.
    /// </summary>
    public static bool InjectText(string text, bool autoEnter = false)
    {
        Clipboard.SetText(text);

        var target = TryGetSuitableTargetWindow(TargetRetryAttempts, TargetRetryDelayMs);
        if (target == IntPtr.Zero)
        {
            Log.Info("No paste target detected, text copied to clipboard only");
            return false;
        }

        // Small delay to let the clipboard settle
        Thread.Sleep(ClipboardSettleDelayMs);

        SendCtrlV();

        if (autoEnter)
        {
            // Give target apps time to process paste before submit.
            Thread.Sleep(PostPasteBeforeEnterDelayMs);
            if (!SendEnter())
                Log.Info("Auto-enter skipped because no suitable target was focused after paste.");
        }

        return true;
    }

    /// <summary>
    /// Simulates pressing Enter in the currently focused application.
    /// Returns true if sent, false when no suitable target is focused.
    /// </summary>
    public static bool SendEnter()
    {
        var target = TryGetSuitableTargetWindow(TargetRetryAttempts, TargetRetryDelayMs);
        if (target == IntPtr.Zero)
        {
            Log.Info("No suitable target detected for Enter key");
            return false;
        }

        SendEnterKey();
        return true;
    }

    private static IntPtr TryGetSuitableTargetWindow(int attempts, int delayMs)
    {
        var clampedAttempts = Math.Max(1, attempts);
        for (var attempt = 0; attempt < clampedAttempts; attempt++)
        {
            var fg = GetForegroundWindow();
            if (IsSuitableTargetWindow(fg))
                return fg;

            if (attempt < clampedAttempts - 1)
                Thread.Sleep(Math.Max(0, delayMs));
        }

        return IntPtr.Zero;
    }

    private static bool IsSuitableTargetWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;
        if (handle == GetDesktopWindow())
            return false;
        if (handle == GetShellWindow())
            return false;

        // Check for common "no target" windows.
        var sb = new StringBuilder(256);
        GetClassName(handle, sb, 256);
        var className = sb.ToString();

        // Progman / WorkerW = desktop wallpaper, Shell_TrayWnd = taskbar.
        return className is not ("Progman" or "WorkerW" or "Shell_TrayWnd");
    }

    private static bool DoesWindowHaveTextInTextInputTarget(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
            return false;

        var focusedWindow = GetFocus();
        if (focusedWindow != IntPtr.Zero && IsLikelyTextInputWindow(focusedWindow))
            return DoesWindowHaveText(focusedWindow);

        if (IsLikelyTextInputWindow(targetWindow))
            return DoesWindowHaveText(targetWindow);

        return false;
    }

    private static bool IsLikelyTextInputWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;

        var className = GetWindowClassName(handle);
        if (string.IsNullOrWhiteSpace(className))
            return false;

        if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase))
            return true;

        if (className.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (className.Contains("RICHEDIT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (className.Contains("EDIT", StringComparison.OrdinalIgnoreCase))
            return true;

        return className.Contains("TEXT", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowClassName(IntPtr handle)
    {
        var sb = new StringBuilder(256);
        _ = GetClassName(handle, sb, 256);
        return sb.ToString();
    }

    private static bool DoesWindowHaveText(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return false;

        try
        {
            if (GetWindowTextLength(handle) > 0)
                return true;

            return SendMessage(handle, WM_GETTEXTLENGTH, 0, 0) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            CreateKeyDown(VK_CONTROL),
            CreateKeyDown(VK_V),
            CreateKeyUp(VK_V),
            CreateKeyUp(VK_CONTROL)
        };

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length)
            return;

        // Fallback for environments where SendInput may be blocked.
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static void SendEnterKey()
    {
        var inputs = new[]
        {
            CreateKeyDown(VK_RETURN),
            CreateKeyUp(VK_RETURN)
        };

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length)
            return;

        // Fallback for environments where SendInput may be blocked.
        keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    private static INPUT CreateKeyDown(byte virtualKey)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = 0
                }
            }
        };
    }

    private static INPUT CreateKeyUp(byte virtualKey)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = KEYEVENTF_KEYUP
                }
            }
        };
    }
}
