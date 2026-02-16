using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

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
    private const int GW_CHILD = 5;
    private const int GW_HWNDNEXT = 2;
    private const int UiAutomationMaxTextLength = 8192;

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

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
    /// Returns true when focus is on a control that is likely a text-input target.
    /// </summary>
    public static bool HasLikelyTextInputTarget()
    {
        var target = TryGetSuitableTargetWindow(TargetRetryAttempts, TargetRetryDelayMs);
        if (target == IntPtr.Zero)
            return false;

        var focusedWindow = GetFocusedWindowInForegroundThread(target);
        if (focusedWindow == IntPtr.Zero)
            return false;

        return IsLikelyTextInputWindow(focusedWindow, out var confidence) &&
               confidence != TextInputConfidence.None;
    }

    /// <summary>
    /// Returns true when the active suitable target appears to already contain text.
    /// </summary>
    public static bool TargetHasExistingText()
    {
        var debugEnabled = Log.IsEnabled;
        var target = TryGetSuitableTargetWindow(TargetRetryAttempts, TargetRetryDelayMs);
        if (target == IntPtr.Zero)
        {
            if (debugEnabled)
            {
                var fg = GetForegroundWindow();
                var fgClass = fg == IntPtr.Zero ? string.Empty : GetWindowClassName(fg);
                Log.Info($"[TextInjector] TargetHasExistingText: no suitable target. foreground={FormatHandle(fg)} class={fgClass}");
            }
            return false;
        }

        if (debugEnabled)
        {
            var className = GetWindowClassName(target);
            var threadId = GetWindowThreadProcessId(target, out var processId);
            var processName = TryGetProcessName(processId);
            var processLabel = string.IsNullOrWhiteSpace(processName) ? string.Empty : $" ({processName})";
            Log.Info($"[TextInjector] TargetHasExistingText: target={FormatHandle(target)} class={className} tid={threadId} pid={processId}{processLabel}");
        }

        var focusedWindow = GetFocusedWindowInForegroundThread(target);
        var candidate = focusedWindow == IntPtr.Zero ? target : focusedWindow;
        if (debugEnabled)
        {
            var candidateClass = GetWindowClassName(candidate);
            _ = IsLikelyTextInputWindow(candidate, out var confidence);
            Log.Info($"[TextInjector] TargetHasExistingText: candidate={FormatHandle(candidate)} class={candidateClass} confidence={confidence}");
        }

        var hasText = DoesWindowHaveTextInTextInputTarget(
            focusedWindow == IntPtr.Zero ? target : focusedWindow,
            foregroundWindow: target);

        if (debugEnabled)
            Log.Info($"[TextInjector] TargetHasExistingText: result={hasText}");

        return hasText;
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

    private static bool DoesWindowHaveTextInTextInputTarget(IntPtr targetWindow, IntPtr foregroundWindow)
    {
        if (targetWindow == IntPtr.Zero)
            return false;

        var debugEnabled = Log.IsEnabled;
        var className = debugEnabled ? GetWindowClassName(targetWindow) : string.Empty;
        var isTextInput = IsLikelyTextInputWindow(targetWindow, out var confidence);
        if (isTextInput && confidence is TextInputConfidence.Strong)
        {
            if (debugEnabled)
                Log.Info($"[TextInjector] ExistingText probe: hwnd={FormatHandle(targetWindow)} class={className} path=win32");
            return DoesWindowHaveText(targetWindow);
        }

        // For Chromium/Electron/WPF/etc, the focused UIA element is a better signal than window text APIs.
        if (debugEnabled)
            Log.Info($"[TextInjector] ExistingText probe: hwnd={FormatHandle(targetWindow)} class={className} confidence={confidence} path=uia");
        return DoesFocusedUiAutomationTextInputHaveExistingText(foregroundWindow);
    }

    private static IntPtr GetFocusedWindowInForegroundThread(IntPtr fallbackWindow)
    {
        var debugEnabled = Log.IsEnabled;
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
            return FindLikelyTextInputWindowOrFallback(fallbackWindow);

        var foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
        if (foregroundThreadId == 0)
            return FindLikelyTextInputWindowOrFallback(fallbackWindow);

        if (debugEnabled)
        {
            var fgClass = GetWindowClassName(foregroundWindow);
            Log.Info($"[TextInjector] GUIThreadInfo: foreground={FormatHandle(foregroundWindow)} class={fgClass} thread={foregroundThreadId}");
        }

        var guiInfo = new GUITHREADINFO
        {
            cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>()
        };

        if (!GetGUIThreadInfo(foregroundThreadId, ref guiInfo))
            return FindLikelyTextInputWindowOrFallback(fallbackWindow);

        if (debugEnabled)
        {
            var activeClass = guiInfo.hwndActive == IntPtr.Zero ? string.Empty : GetWindowClassName(guiInfo.hwndActive);
            var focusClass = guiInfo.hwndFocus == IntPtr.Zero ? string.Empty : GetWindowClassName(guiInfo.hwndFocus);
            Log.Info($"[TextInjector] GUIThreadInfo: active={FormatHandle(guiInfo.hwndActive)} class={activeClass} focus={FormatHandle(guiInfo.hwndFocus)} class={focusClass}");
        }

        if (guiInfo.hwndFocus != IntPtr.Zero &&
            IsLikelyTextInputWindow(guiInfo.hwndFocus, out var focusedConfidence) &&
            focusedConfidence == TextInputConfidence.Strong)
        {
            if (debugEnabled)
                Log.Info($"[TextInjector] GUIThreadInfo: using strong hwndFocus={FormatHandle(guiInfo.hwndFocus)}");
            return guiInfo.hwndFocus;
        }

        return FindLikelyTextInputWindowOrFallback(fallbackWindow);
    }

    private static IntPtr FindLikelyTextInputWindowOrFallback(IntPtr fallbackWindow)
    {
        if (fallbackWindow == IntPtr.Zero)
            return IntPtr.Zero;

        var debugEnabled = Log.IsEnabled;
        var weakFallback = IntPtr.Zero;
        var scannedChildren = 0;
        var directChild = GetWindow(fallbackWindow, GW_CHILD);
        while (directChild != IntPtr.Zero)
        {
            scannedChildren++;
            if (IsLikelyTextInputWindow(directChild, out var confidence))
            {
                if (confidence == TextInputConfidence.Strong)
                {
                    if (debugEnabled)
                    {
                        var childClass = GetWindowClassName(directChild);
                        Log.Info($"[TextInjector] Focus fallback scan: selected strong child={FormatHandle(directChild)} class={childClass} scanned={scannedChildren}");
                    }
                    return directChild;
                }

                if (weakFallback == IntPtr.Zero)
                    weakFallback = directChild;
            }

            directChild = GetWindow(directChild, GW_HWNDNEXT);
        }

        var selected = weakFallback != IntPtr.Zero ? weakFallback : fallbackWindow;
        if (debugEnabled)
        {
            var fallbackClass = GetWindowClassName(fallbackWindow);
            var weakClass = weakFallback == IntPtr.Zero ? string.Empty : GetWindowClassName(weakFallback);
            var selectedClass = GetWindowClassName(selected);
            Log.Info($"[TextInjector] Focus fallback scan: fallback={FormatHandle(fallbackWindow)} class={fallbackClass} scanned={scannedChildren} weak={FormatHandle(weakFallback)} class={weakClass} selected={FormatHandle(selected)} class={selectedClass}");
        }

        return selected;
    }

    private enum TextInputConfidence
    {
        None,
        Weak,
        Strong
    }

    private static bool IsLikelyTextInputWindow(IntPtr handle, out TextInputConfidence confidence)
    {
        if (handle == IntPtr.Zero)
        {
            confidence = TextInputConfidence.None;
            return false;
        }

        var className = GetWindowClassName(handle);
        if (string.IsNullOrWhiteSpace(className))
        {
            confidence = TextInputConfidence.None;
            return false;
        }

        if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase))
        {
            confidence = TextInputConfidence.Strong;
            return true;
        }

        if (className.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase))
        {
            confidence = TextInputConfidence.Strong;
            return true;
        }

        if (className.Contains("RICHEDIT", StringComparison.OrdinalIgnoreCase))
        {
            confidence = TextInputConfidence.Strong;
            return true;
        }

        if (className.Contains("EDIT", StringComparison.OrdinalIgnoreCase))
        {
            confidence = TextInputConfidence.Strong;
            return true;
        }

        if (className.Contains("SCINTILLA", StringComparison.OrdinalIgnoreCase))
        {
            confidence = TextInputConfidence.Strong;
            return true;
        }

        if (className.Contains("Chrome_RenderWidgetHost", StringComparison.OrdinalIgnoreCase))
        {
            confidence = TextInputConfidence.Weak;
            return true;
        }

        confidence = TextInputConfidence.None;
        return false;
    }

    private static bool DoesFocusedUiAutomationTextInputHaveExistingText(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero)
            return false;

        var debugEnabled = Log.IsEnabled;
        try
        {
            AutomationElement? focused;
            try
            {
                focused = AutomationElement.FocusedElement;
            }
            catch (Exception ex)
            {
                if (debugEnabled)
                    Log.Info($"[TextInjector/UIA] FocusedElement failed: ex={ex.GetType().Name}");
                return false;
            }

            if (focused == null)
            {
                if (debugEnabled)
                    Log.Info("[TextInjector/UIA] No focused element.");
                return false;
            }

            var inForeground = IsAutomationElementInForegroundWindow(
                focused,
                foregroundWindow,
                out var resolvedWindow,
                out var resolvedDepth);
            if (debugEnabled)
            {
                var focusedSummary = DescribeAutomationElement(focused);
                Log.Info($"[TextInjector/UIA] Focused element: {focusedSummary} resolvedHwnd={FormatHandle(resolvedWindow)} hwndDepth={resolvedDepth} inForeground={inForeground} foreground={FormatHandle(foregroundWindow)}");
            }

            if (!inForeground)
                return false;

            if (TryGetEditableTextFromFocusedAutomationElement(
                     focused,
                     out var text,
                     out var source,
                     out var isReadOnly,
                     out var skipReason))
            {
                var meaningful = HasMeaningfulText(text);
                if (debugEnabled)
                {
                    var note = string.IsNullOrWhiteSpace(skipReason) ? string.Empty : $" note={skipReason}";
                    Log.Info($"[TextInjector/UIA] Text probe: depth=0 source={source} readOnly={isReadOnly} textLen={text.Length} meaningful={meaningful}{note}");
                }
                return meaningful;
            }

            if (debugEnabled && !string.IsNullOrWhiteSpace(skipReason))
                Log.Info($"[TextInjector/UIA] Text probe: depth=0 skipped ({skipReason})");

            // Some providers place focus on a child within the editable region; walk up a few ancestors.
            var current = focused;
            for (var depth = 1; depth <= 8; depth++)
            {
                current = SafeGet(() => TreeWalker.RawViewWalker.GetParent(current), null);
                if (current == null)
                    break;

                if (TryGetEditableTextFromFocusedAutomationElement(
                        current,
                        out text,
                        out source,
                        out isReadOnly,
                        out _))
                {
                    var meaningful = HasMeaningfulText(text);
                    if (debugEnabled)
                    {
                        var elementSummary = DescribeAutomationElement(current);
                        Log.Info($"[TextInjector/UIA] Text probe: depth={depth} source={source} readOnly={isReadOnly} textLen={text.Length} meaningful={meaningful} element={elementSummary}");
                    }
                    return meaningful;
                }
            }

            if (debugEnabled)
                Log.Info("[TextInjector/UIA] Text probe: no editable text found on focused element or ancestors.");
        }
        catch (Exception ex)
        {
            // Best effort only.
            if (debugEnabled)
                Log.Info($"[TextInjector/UIA] Exception: ex={ex.GetType().Name}");
        }

        return false;
    }

    private static bool IsAutomationElementInForegroundWindow(
        AutomationElement element,
        IntPtr foregroundWindow,
        out IntPtr resolvedWindow,
        out int resolvedDepth)
    {
        resolvedWindow = IntPtr.Zero;
        resolvedDepth = -1;
        try
        {
            var current = element;
            for (var depth = 0; depth < 64 && current != null; depth++)
            {
                var hwnd = current.Current.NativeWindowHandle;
                if (hwnd != 0)
                {
                    resolvedWindow = (IntPtr)hwnd;
                    resolvedDepth = depth;
                    return resolvedWindow == foregroundWindow || IsChild(foregroundWindow, resolvedWindow);
                }

                current = TreeWalker.RawViewWalker.GetParent(current);
            }
        }
        catch
        {
            // Ignore UIA failures.
        }

        return false;
    }

    private enum UiAutomationTextSource
    {
        None,
        ValuePattern,
        TextPattern
    }

    private static bool TryGetEditableTextFromFocusedAutomationElement(
        AutomationElement element,
        out string text,
        out UiAutomationTextSource source,
        out bool isReadOnly,
        out string skipReason)
    {
        text = string.Empty;
        source = UiAutomationTextSource.None;
        isReadOnly = true;
        skipReason = string.Empty;

        if (IsPasswordAutomationElement(element))
        {
            skipReason = "password";
            return false;
        }

        bool isEnabled;
        try
        {
            isEnabled = element.Current.IsEnabled;
        }
        catch (Exception ex)
        {
            skipReason = $"element-unavailable({ex.GetType().Name})";
            return false;
        }

        if (!isEnabled)
        {
            skipReason = "disabled";
            return false;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) &&
            valuePatternObject is ValuePattern valuePattern &&
            valuePattern.Current.IsReadOnly is false)
        {
            source = UiAutomationTextSource.ValuePattern;
            isReadOnly = valuePattern.Current.IsReadOnly;
            text = valuePattern.Current.Value ?? string.Empty;
            return true;
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out valuePatternObject) &&
            valuePatternObject is ValuePattern readOnlyValuePattern &&
            readOnlyValuePattern.Current.IsReadOnly)
        {
            var value = readOnlyValuePattern.Current.Value ?? string.Empty;
            skipReason = $"value-readonly(len={value.Length})";
            return false;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObject) &&
            textPatternObject is TextPattern textPattern &&
            IsEditableTextPatternElement(element))
        {
            source = UiAutomationTextSource.TextPattern;
            isReadOnly = false;
            text = textPattern.DocumentRange.GetText(UiAutomationMaxTextLength) ?? string.Empty;
            if (TryStripProseMirrorPlaceholderText(element, ref text, out var placeholderNote))
                skipReason = placeholderNote;
            return true;
        }

        if (element.TryGetCurrentPattern(TextPattern.Pattern, out _))
        {
            var controlType = SafeGet(() => element.Current.ControlType?.ProgrammaticName ?? "?", "?");
            skipReason = $"textpattern-non-edit(ct={controlType})";
            return false;
        }

        skipReason = "no-editable-pattern";
        return false;
    }

    private static bool IsEditableTextPatternElement(AutomationElement element)
    {
        // Avoid treating focused "document/page" text as an input target; prefer Edit-like UIA controls.
        // Some Chromium/Electron editors (for example ProseMirror) expose the editable region as a Group/Document
        // with TextPattern instead of ControlType.Edit.
        var controlType = SafeGet(() => element.Current.ControlType, null);
        if (controlType == ControlType.Edit)
            return true;

        if (controlType != ControlType.Group && controlType != ControlType.Document && controlType != ControlType.Pane)
            return false;

        var hasFocus = SafeGet(() => element.Current.HasKeyboardFocus, false);
        var focusable = SafeGet(() => element.Current.IsKeyboardFocusable, false);
        if (!hasFocus || !focusable)
            return false;

        var className = SafeGet(() => element.Current.ClassName ?? string.Empty, string.Empty);
        if (className.IndexOf("ProseMirror", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool TryStripProseMirrorPlaceholderText(AutomationElement element, ref string text, out string note)
    {
        note = string.Empty;

        if (string.IsNullOrEmpty(text))
            return false;

        if (!IsProseMirrorAutomationElement(element))
            return false;

        var normalizedText = NormalizeAutomationText(text);
        if (normalizedText.Length == 0)
            return false;

        var helpText = SafeGet(() => element.Current.HelpText ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(helpText))
        {
            var normalizedHelp = NormalizeAutomationText(helpText);
            if (MatchesPlaceholderCandidate(normalizedText, normalizedHelp))
            {
                text = string.Empty;
                note = $"placeholder-helptext(len={normalizedText.Length})";
                return true;
            }
        }

        var name = SafeGet(() => element.Current.Name ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(name))
        {
            var normalizedName = NormalizeAutomationText(name);
            if (MatchesPlaceholderCandidate(normalizedText, normalizedName))
            {
                text = string.Empty;
                note = $"placeholder-name(len={normalizedText.Length})";
                return true;
            }
        }

        var itemStatus = SafeGet(() => element.Current.ItemStatus ?? string.Empty, string.Empty);
        if (!string.IsNullOrWhiteSpace(itemStatus))
        {
            var normalizedStatus = NormalizeAutomationText(itemStatus);
            if (MatchesPlaceholderCandidate(normalizedText, normalizedStatus))
            {
                text = string.Empty;
                note = $"placeholder-itemstatus(len={normalizedText.Length})";
                return true;
            }
        }

        if (LooksLikeProseMirrorPlaceholderText(normalizedText))
        {
            text = string.Empty;
            note = $"placeholder-heuristic(len={normalizedText.Length})";
            return true;
        }

        return false;
    }

    private static bool MatchesPlaceholderCandidate(string normalizedText, string normalizedCandidate)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(normalizedCandidate))
            return false;

        if (normalizedText.Equals(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        // Some providers include extra hints/shortcuts in help text. Treat near-equality/containment as placeholder.
        if (normalizedCandidate.IndexOf(normalizedText, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (normalizedText.IndexOf(normalizedCandidate, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool LooksLikeProseMirrorPlaceholderText(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
            return false;

        // Placeholders are short, single-line prompts, often with a keybinding hint.
        if (normalizedText.Length > 96 || normalizedText.IndexOf('\n') >= 0)
            return false;

        if (StartsWithPlaceholderPhrase(normalizedText, "Ask Copilot", out var remainder) ||
            StartsWithPlaceholderPhrase(normalizedText, "Ask for follow-up changes", out remainder) ||
            StartsWithPlaceholderPhrase(normalizedText, "Ask a question", out remainder) ||
            StartsWithPlaceholderPhrase(normalizedText, "Type a message", out remainder) ||
            StartsWithPlaceholderPhrase(normalizedText, "Write a message", out remainder) ||
            StartsWithPlaceholderPhrase(normalizedText, "Send a message", out remainder) ||
            StartsWithPlaceholderPhrase(normalizedText, "Enter a message", out remainder))
        {
            // Reject common user content like "Ask Copilot to ..." or "Write a message to ...".
            return remainder.Length == 0
                   || remainder.StartsWith("(", StringComparison.Ordinal)
                   || remainder.StartsWith("...", StringComparison.Ordinal)
                   || remainder.StartsWith("\u2026", StringComparison.Ordinal);
        }

        // Fallback: keybinding-style placeholders, e.g. "Ask ... (Ctrl+Shift+I)".
        if (normalizedText.IndexOf("(Ctrl+", StringComparison.OrdinalIgnoreCase) >= 0 &&
            normalizedText.EndsWith(")", StringComparison.Ordinal) &&
            (normalizedText.StartsWith("Ask ", StringComparison.OrdinalIgnoreCase)
             || normalizedText.StartsWith("Type ", StringComparison.OrdinalIgnoreCase)
             || normalizedText.StartsWith("Write ", StringComparison.OrdinalIgnoreCase)
             || normalizedText.StartsWith("Send ", StringComparison.OrdinalIgnoreCase)
             || normalizedText.StartsWith("Enter ", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static bool StartsWithPlaceholderPhrase(string text, string phrase, out string remainder)
    {
        remainder = string.Empty;

        if (!text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
            return false;

        remainder = text.Substring(phrase.Length).TrimStart();
        return true;
    }

    private static bool IsProseMirrorAutomationElement(AutomationElement element)
    {
        var className = SafeGet(() => element.Current.ClassName ?? string.Empty, string.Empty);
        return className.IndexOf("ProseMirror", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string NormalizeAutomationText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var previousWasSpace = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '\r')
                continue;

            if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
                continue;

            if (char.IsControl(ch))
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (previousWasSpace)
                    continue;
                sb.Append(' ');
                previousWasSpace = true;
                continue;
            }

            sb.Append(ch);
            previousWasSpace = false;
        }

        return sb.ToString().Trim();
    }

    private static bool IsPasswordAutomationElement(AutomationElement element)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty);
            return value is bool isPassword && isPassword;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyTextInputWindow(IntPtr handle)
    {
        return IsLikelyTextInputWindow(handle, out _);
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
            var debugEnabled = Log.IsEnabled;
            var textLength = GetWindowTextLength(handle);
            var hasWindowText = HasReadableNonWhitespaceText(handle, textLength, out var requested1, out var actual1);

            var messageTextLength = SendMessage(handle, WM_GETTEXTLENGTH, 0, 0);
            var hasMessageText = HasReadableNonWhitespaceText(handle, messageTextLength, out var requested2, out var actual2);
            var result = hasWindowText || hasMessageText;

            if (debugEnabled)
            {
                var className = GetWindowClassName(handle);
                Log.Info(
                    $"[TextInjector] Win32 text probe: hwnd={FormatHandle(handle)} class={className} " +
                    $"GetWindowTextLength={textLength} requested={requested1} actual={actual1} " +
                    $"WM_GETTEXTLENGTH={messageTextLength} requested2={requested2} actual2={actual2} meaningful={result}");
            }

            return result;
        }
        catch
        {
            if (Log.IsEnabled)
            {
                var className = GetWindowClassName(handle);
                Log.Info($"[TextInjector] Win32 text probe failed: hwnd={FormatHandle(handle)} class={className}");
            }
            return false;
        }
    }

    private static bool HasReadableNonWhitespaceText(
        IntPtr handle,
        int textLength,
        out int requestedLength,
        out int actualLength)
    {
        requestedLength = 0;
        actualLength = 0;

        if (textLength <= 0)
            return false;

        requestedLength = Math.Max(1, Math.Min(textLength + 1, 8192));
        var sb = new StringBuilder(requestedLength);
        actualLength = GetWindowText(handle, sb, requestedLength);
        if (actualLength <= 0)
            return false;

        var text = sb.ToString(0, actualLength);
        return HasMeaningfulText(text);
    }

    private static bool HasMeaningfulText(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
                continue;

            // Remove common invisible formatting characters that some controls report.
            if (ch is '\u200B' or '\u200C' or '\u200D' or '\uFEFF')
                continue;

            return true;
        }

        return false;
    }

    private static string FormatHandle(IntPtr handle)
    {
        return handle == IntPtr.Zero ? "0x0" : $"0x{handle.ToInt64():X}";
    }

    private static string TryGetProcessName(uint processId)
    {
        if (processId == 0)
            return string.Empty;

        try
        {
            return System.Diagnostics.Process.GetProcessById(unchecked((int)processId)).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string DescribeAutomationElement(AutomationElement element)
    {
        try
        {
            var hwnd = element.Current.NativeWindowHandle;
            var pid = element.Current.ProcessId;
            var controlType = element.Current.ControlType?.ProgrammaticName ?? "?";
            var className = element.Current.ClassName ?? string.Empty;
            var enabled = element.Current.IsEnabled;
            var hasFocus = element.Current.HasKeyboardFocus;
            var focusable = element.Current.IsKeyboardFocusable;
            return $"ct={controlType} class={className} pid={pid} hwnd=0x{hwnd:X} enabled={enabled} focus={hasFocus} kbdFocusable={focusable}";
        }
        catch (Exception ex)
        {
            return $"<unavailable:{ex.GetType().Name}>";
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
