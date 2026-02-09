using System.Runtime.InteropServices;
using System.Text;

namespace VoiceType;

public class TrayContext : ApplicationContext
{
    private const int PRIMARY_HOTKEY_ID = 1;
    private const int PEN_HOTKEY_ID = 2;
    private const int MOD_NONE = 0x0000;
    private const int MOD_CTRL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_SPACE = 0x20;
    private const string PrimaryHotkeyDisplayName = "Ctrl+Shift+Space";

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    private const int SW_RESTORE = 9;

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly AudioRecorder _recorder;
    private readonly OverlayForm _overlay;
    private readonly Icon _appIcon;
    private readonly ToolStripMenuItem _versionMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _startedAtMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _uptimeMenuItem = new() { Enabled = false };
    private TranscriptionService? _transcriptionService;
    private bool _autoEnter;
    private bool _enableOverlayPopups = true;
    private int _overlayDurationMs = AppConfig.DefaultOverlayDurationMs;
    private bool _enablePenHotkey;
    private string _penHotkey = AppConfig.DefaultPenHotkey;
    private bool _penHotkeyRegistered;
    private bool _enableOpenSettingsVoiceCommand;
    private bool _enableExitAppVoiceCommand;
    private bool _enableToggleAutoEnterVoiceCommand;
    private bool _isRecording;
    private bool _isTranscribing;
    private bool _eventsHooked;

    public TrayContext()
    {
        Log.Info("VoiceType starting...");

        _recorder = new AudioRecorder();
        _overlay = new OverlayForm();
        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _appIcon = extractedIcon != null
            ? (Icon)extractedIcon.Clone()
            : (Icon)SystemIcons.Application.Clone();
        extractedIcon?.Dispose();
        LoadTranscriptionService();

        _trayIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "VoiceType - Ready",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        HookShutdownEvents();

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;
        RefreshHotkeyRegistration();

        if (_transcriptionService == null)
        {
            Log.Info("No API key configured");
            ShowOverlay("No API key — right-click tray icon > Settings", Color.Orange, 5000);
        }
        else
        {
            ShowOverlay($"VoiceType ready — {BuildHotkeyHint()} to dictate", Color.LightGreen, 2000);
        }

        Log.Info("VoiceType started successfully");
    }

    private void LoadTranscriptionService()
    {
        var config = AppConfig.Load();
        Log.Configure(config.EnableDebugLogging);
        _autoEnter = config.AutoEnter;
        _enableOverlayPopups = config.EnableOverlayPopups;
        _overlayDurationMs = AppConfig.NormalizeOverlayDuration(config.OverlayDurationMs);
        _enablePenHotkey = config.EnablePenHotkey;
        _penHotkey = AppConfig.NormalizePenHotkey(config.PenHotkey);
        _enableOpenSettingsVoiceCommand = config.EnableOpenSettingsVoiceCommand;
        _enableExitAppVoiceCommand = config.EnableExitAppVoiceCommand;
        _enableToggleAutoEnterVoiceCommand = config.EnableToggleAutoEnterVoiceCommand;
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _transcriptionService = new TranscriptionService(config.ApiKey, config.Model);
        else
            _transcriptionService = null;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        UpdateRuntimeMenuItems();
        menu.Opening += (_, _) => UpdateRuntimeMenuItems();
        menu.Items.Add(_versionMenuItem);
        menu.Items.Add(_startedAtMenuItem);
        menu.Items.Add(_uptimeMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings...", null, OnSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);
        return menu;
    }

    private void UpdateRuntimeMenuItems()
    {
        _versionMenuItem.Text = $"Version: {AppInfo.Version}";
        _startedAtMenuItem.Text = $"Started: {AppInfo.StartedAtLocal:yyyy-MM-dd HH:mm:ss}";
        _uptimeMenuItem.Text = $"Uptime: {AppInfo.FormatUptime(AppInfo.Uptime)}";
    }

    private async void OnHotkeyPressed(object? sender, HotkeyPressedEventArgs e)
    {
        if (e.HotkeyId is not PRIMARY_HOTKEY_ID and not PEN_HOTKEY_ID)
            return;

        if (_isTranscribing)
        {
            ShowOverlay("Still processing previous dictation...", Color.CornflowerBlue, 2000);
            return;
        }

        if (_transcriptionService == null)
        {
            ShowOverlay("No API key configured — check Settings", Color.Orange);
            return;
        }

        if (_isRecording)
        {
            // Stop recording and transcribe
            _isRecording = false;
            _trayIcon.Icon = _appIcon;
            _trayIcon.Text = "VoiceType - Transcribing...";
            ShowOverlay("Processing voice...", Color.CornflowerBlue, 10000);
            Log.Info("Recording stopped, starting transcription...");
            _isTranscribing = true;

            try
            {
                var audioData = _recorder.Stop();
                var metrics = _recorder.LastCaptureMetrics;
                Log.Info(
                    $"Audio captured: {audioData.Length:N0} bytes, duration={metrics.Duration.TotalSeconds:F2}s, " +
                    $"rms={metrics.Rms:F4}, peak={metrics.Peak:F4}, active={metrics.ActiveSampleRatio:P1}");

                if (metrics.IsLikelySilence)
                {
                    Log.Info("Skipping transcription because captured audio appears to be silence/noise.");
                    ShowOverlay("No speech detected", Color.Gray, 2000);
                    return;
                }

                var text = await _transcriptionService.TranscribeAsync(audioData);
                Log.Info($"Transcription completed ({text.Length} chars)");

                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Check for voice commands before pasting
                    var command = ParseVoiceCommand(text);
                    if (command != null)
                    {
                        Log.Info($"Voice command detected: {command}");
                        HandleVoiceCommand(command);
                        return;
                    }

                    var pasted = TextInjector.InjectText(text, _autoEnter);

                    if (pasted)
                    {
                        Log.Info("Text injected via clipboard");
                        ShowOverlay(text, Color.LightGreen, 4000);
                    }
                    else
                    {
                        Log.Info("No paste target, text on clipboard");
                        ShowOverlay(text + "\n(copied to clipboard — Ctrl+V to paste)", Color.Gold, 5000);
                    }
                }
                else
                {
                    ShowOverlay("No speech detected", Color.Gray, 2000);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Transcription failed", ex);
                ShowOverlay("Error: " + ex.Message, Color.Salmon, 4000);
            }
            finally
            {
                _isTranscribing = false;
                SetReadyState();
            }
        }
        else
        {
            // Start recording
            try
            {
                _recorder.Start();
                _isRecording = true;
                _trayIcon.Icon = _appIcon;
                _trayIcon.Text = $"VoiceType - Recording... ({BuildHotkeyHint()} to stop)";
                ShowOverlay("Listening... speak now!", Color.CornflowerBlue, 30000);
                Log.Info("Recording started");
            }
            catch (Exception ex)
            {
                _isRecording = false;
                Log.Error("Failed to start recording", ex);
                ShowOverlay("Microphone error: " + ex.Message, Color.Salmon, 4000);
                SetReadyState();
            }
        }
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        if (_overlay.InvokeRequired)
        {
            _overlay.Invoke(new Action(() => OnSettings(sender, e)));
            return;
        }

        var previousForegroundWindow = GetForegroundWindow();
        _overlay.Hide();

        IntPtr settingsWindow = IntPtr.Zero;
        using var dlg = new SettingsForm();
        dlg.Shown += (_, _) =>
        {
            dlg.BeginInvoke(new Action(() =>
            {
                settingsWindow = dlg.Handle;
                dlg.TopMost = true;
                dlg.Activate();
                dlg.BringToFront();
                SetForegroundWindow(dlg.Handle);
                dlg.TopMost = false;
            }));
        };
        dlg.ShowDialog();
        LoadTranscriptionService();
        RefreshHotkeyRegistration();
        SetReadyState();
        RestorePreviousFocus(previousForegroundWindow, settingsWindow);
    }

    private void OnExit(object? sender, EventArgs e) => Shutdown();

    private void Shutdown()
    {
        EnsureTrayIconHidden();
        Application.Exit();
    }

    private void SetReadyState()
    {
        _trayIcon.Icon = _appIcon;
        _trayIcon.Text = $"VoiceType - Ready ({BuildHotkeyHint()})";
    }

    private string BuildHotkeyHint()
    {
        if (_penHotkeyRegistered)
            return $"{PrimaryHotkeyDisplayName} or {_penHotkey}";

        return PrimaryHotkeyDisplayName;
    }

    private void RefreshHotkeyRegistration()
    {
        UnregisterHotkeys();

        if (!RegisterHotKey(_hotkeyWindow.Handle, PRIMARY_HOTKEY_ID, MOD_CTRL | MOD_SHIFT, VK_SPACE))
        {
            Log.Error($"Failed to register hotkey {PrimaryHotkeyDisplayName}");
            MessageBox.Show(
                $"Failed to register hotkey {PrimaryHotkeyDisplayName}.\nAnother app may be using it.",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        else
        {
            Log.Info($"Hotkey registered: {PrimaryHotkeyDisplayName}");
        }

        _penHotkeyRegistered = false;
        if (_enablePenHotkey && AppConfig.TryGetVirtualKeyForPenHotkey(_penHotkey, out var penVk))
        {
            if (RegisterHotKey(_hotkeyWindow.Handle, PEN_HOTKEY_ID, MOD_NONE, penVk))
            {
                _penHotkeyRegistered = true;
                Log.Info($"Surface Pen hotkey registered: {_penHotkey}");
            }
            else
            {
                Log.Info($"Surface Pen hotkey unavailable: {_penHotkey}");
                ShowOverlay(
                    $"Could not register Surface Pen hotkey ({_penHotkey})",
                    Color.Orange,
                    3000);
            }
        }

        SetReadyState();
    }

    private void UnregisterHotkeys()
    {
        _ = UnregisterHotKey(_hotkeyWindow.Handle, PRIMARY_HOTKEY_ID);
        _ = UnregisterHotKey(_hotkeyWindow.Handle, PEN_HOTKEY_ID);
        _penHotkeyRegistered = false;
    }

    private void ShowOverlay(string text, Color? color = null, int durationMs = 3000)
    {
        if (!_enableOverlayPopups)
            return;

        _ = durationMs;
        _overlay.ShowMessage(text, color, _overlayDurationMs);
    }

    private string? ParseVoiceCommand(string text)
    {
        return VoiceCommandParser.Parse(
            text,
            _enableOpenSettingsVoiceCommand,
            _enableExitAppVoiceCommand,
            _enableToggleAutoEnterVoiceCommand);
    }

    private void HandleVoiceCommand(string command)
    {
        switch (command)
        {
            case VoiceCommandParser.Exit:
                ShowOverlay("Goodbye!", Color.LightGreen, 1000);
                _ = Task.Delay(800).ContinueWith(_ => Invoke(Shutdown));
                break;
            case VoiceCommandParser.Settings:
                ShowOverlay("Opening settings...", Color.CornflowerBlue, 1000);
                OnSettings(null, EventArgs.Empty);
                break;
            case VoiceCommandParser.EnableAutoEnter:
                SetAutoEnter(true);
                break;
            case VoiceCommandParser.DisableAutoEnter:
                SetAutoEnter(false);
                break;
        }
    }

    private void SetAutoEnter(bool enabled)
    {
        if (_autoEnter == enabled)
        {
            var existingStateLabel = enabled ? "enabled" : "disabled";
            ShowOverlay($"Auto-enter already {existingStateLabel}", Color.Gray, 1200);
            return;
        }

        try
        {
            var config = AppConfig.Load();
            config.AutoEnter = enabled;
            config.Save();
            _autoEnter = enabled;

            var stateLabel = enabled ? "enabled" : "disabled";
            ShowOverlay($"Auto-enter {stateLabel}", Color.LightGreen, 1500);
            Log.Info($"Auto-enter set via voice command ({stateLabel})");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update auto-enter setting via voice command", ex);
            ShowOverlay("Failed to update auto-enter setting", Color.Salmon, 2000);
        }
    }

    private void Invoke(Action action)
    {
        if (_overlay.InvokeRequired)
            _overlay.Invoke(action);
        else
            action();
    }

    private void HookShutdownEvents()
    {
        if (_eventsHooked)
            return;

        _eventsHooked = true;
        Application.ApplicationExit += OnApplicationExit;
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    private void UnhookShutdownEvents()
    {
        if (!_eventsHooked)
            return;

        _eventsHooked = false;
        Application.ApplicationExit -= OnApplicationExit;
        Application.ThreadException -= OnThreadException;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }

    private void OnApplicationExit(object? sender, EventArgs e) => EnsureTrayIconHidden();

    private void OnThreadException(object sender, ThreadExceptionEventArgs e) => EnsureTrayIconHidden();

    private void OnProcessExit(object? sender, EventArgs e) => EnsureTrayIconHidden();

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) => EnsureTrayIconHidden();

    private void EnsureTrayIconHidden()
    {
        try
        {
            _trayIcon.Visible = false;
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private void RestorePreviousFocus(IntPtr previousWindow, IntPtr settingsWindow)
    {
        if (previousWindow == IntPtr.Zero)
            return;

        if (previousWindow == settingsWindow)
            return;

        if (!IsWindow(previousWindow))
            return;

        if (previousWindow == GetDesktopWindow() || previousWindow == GetShellWindow())
            return;

        if (previousWindow == _hotkeyWindow.Handle || previousWindow == _overlay.Handle)
            return;

        var classNameBuilder = new StringBuilder(256);
        GetClassName(previousWindow, classNameBuilder, classNameBuilder.Capacity);
        var className = classNameBuilder.ToString();
        if (className is "Progman" or "WorkerW" or "Shell_TrayWnd")
            return;

        // If focus has already moved elsewhere, do not force it back.
        var currentForeground = GetForegroundWindow();
        if (currentForeground != IntPtr.Zero &&
            currentForeground != settingsWindow &&
            currentForeground != _hotkeyWindow.Handle)
            return;

        if (IsIconic(previousWindow))
            _ = ShowWindow(previousWindow, SW_RESTORE);

        _ = SetForegroundWindow(previousWindow);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnhookShutdownEvents();
            EnsureTrayIconHidden();
            UnregisterHotkeys();
            _trayIcon.Dispose();
            _hotkeyWindow.Dispose();
            _overlay.Dispose();
            _recorder.Dispose();
            _appIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Hidden window that receives WM_HOTKEY messages.
/// </summary>
internal class HotkeyWindow : NativeWindow, IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(this, new HotkeyPressedEventArgs(m.WParam.ToInt32()));
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}

internal sealed class HotkeyPressedEventArgs : EventArgs
{
    public HotkeyPressedEventArgs(int hotkeyId)
    {
        HotkeyId = hotkeyId;
    }

    public int HotkeyId { get; }
}

