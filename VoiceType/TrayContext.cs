using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VoiceType;

public class TrayContext : ApplicationContext
{
    private static readonly string[] MicSpinnerFrames = ["|", "/", "-", "\\"];
    private static readonly Color CommandOverlayColor = Color.DeepSkyBlue;

    private const int PRIMARY_HOTKEY_ID = 1;
    private const int PEN_HOTKEY_ID = 2;
    private const int MOD_NONE = 0x0000;
    private const int MOD_CTRL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_SPACE = 0x20;
    private const string PrimaryHotkeyDisplayName = "Ctrl+Shift+Space";
    private const int AdaptiveOverlayBaseMs = 1800;
    private const int AdaptiveOverlayMsPerWord = 320;
    private const int AdaptiveOverlayMaxMs = 22000;
    private const int TranscribedOverlayCancelWindowPaddingMs = 560;

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
    private readonly System.Windows.Forms.Timer _listeningOverlayTimer;
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
    private bool _enableSendVoiceCommand;
    private bool _enableShowVoiceCommandsVoiceCommand;
    private bool _useSimpleMicSpinner;
    private int _micLevelPercent;
    private int _micSpinnerIndex;
    private DateTime _recordingStartedAtUtc;
    private bool _shutdownRequested;
    private bool _isShuttingDown;
    private bool _isRecording;
    private bool _isTranscribing;
    private bool _eventsHooked;
    private bool _promptedForApiKeyOnStartup;
    private int _pendingPastePreviewMessageId;
    private TaskCompletionSource<bool>? _pendingPasteCanceledTcs;
    private DateTime _ignoreListenUntilUtc;
    private int _cancelListenSuppressionMs = AppConfig.DefaultCancelListenSuppressionMs;

    public TrayContext()
    {
        Log.Info("VoiceType starting...");

        _recorder = new AudioRecorder();
        _recorder.InputLevelChanged += OnRecorderInputLevelChanged;
        _listeningOverlayTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _listeningOverlayTimer.Tick += (_, _) => UpdateListeningOverlay();
        _overlay = new OverlayForm();
        _overlay.OverlayTapped += OnOverlayTapped;
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
            PromptForApiKeySetupOnStartup();
        }
        else
        {
            ShowOverlay($"VoiceType ready — {BuildOverlayHotkeyHint()} to dictate", Color.LightGreen, 2000);
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
        _overlay.ApplyHudSettings(
            config.OverlayOpacityPercent,
            config.OverlayWidthPercent,
            config.OverlayFontSizePt,
            config.ShowOverlayBorder);
        _enablePenHotkey = config.EnablePenHotkey;
        _penHotkey = AppConfig.NormalizePenHotkey(config.PenHotkey);
        _enableOpenSettingsVoiceCommand = config.EnableOpenSettingsVoiceCommand;
        _enableExitAppVoiceCommand = config.EnableExitAppVoiceCommand;
        _enableToggleAutoEnterVoiceCommand = config.EnableToggleAutoEnterVoiceCommand;
        _enableSendVoiceCommand = config.EnableSendVoiceCommand;
        _enableShowVoiceCommandsVoiceCommand = config.EnableShowVoiceCommandsVoiceCommand;
        _cancelListenSuppressionMs = AppConfig.NormalizeCancelListenSuppressionMs(config.CancelListenSuppressionMs);
        _useSimpleMicSpinner = config.UseSimpleMicSpinner;
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

        if (_shutdownRequested && !_isRecording)
        {
            Log.Info("Ignoring hotkey because shutdown is pending.");
            return;
        }

        if (e.HotkeyId == PEN_HOTKEY_ID && IsListenSuppressed("pen hotkey"))
            return;

        if (_isTranscribing)
        {
            if (e.HotkeyId == PEN_HOTKEY_ID)
            {
                if (TryCancelPendingPastePreview("pen hotkey"))
                    return;
            }

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
            StopListeningOverlay();
            _trayIcon.Icon = _appIcon;
            _trayIcon.Text = "VoiceType - Transcribing...";
            ShowOverlay("Processing voice...", Color.CornflowerBlue, 0);
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

                    var adaptiveDurationMs = GetAdaptiveTranscribedOverlayDurationMs(text);
                    var canceled = await ShowCancelableTranscribedPreviewAsync(text, adaptiveDurationMs);
                    if (canceled)
                    {
                        Log.Info("Paste canceled by tap during transcribed preview.");
                        ShowOverlay("Paste canceled", Color.Gray, 1000);
                        return;
                    }

                    var pasted = TextInjector.InjectText(text, _autoEnter);
                    if (pasted)
                    {
                        Log.Info("Text injected via clipboard");
                    }
                    else
                    {
                        var fallbackText = text + "\n(copied to clipboard — Ctrl+V to paste)";
                        Log.Info("No paste target, text on clipboard");
                        ShowOverlay(fallbackText, Color.Gold, adaptiveDurationMs);
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
                if (IsLikelyApiKeyError(ex))
                {
                    Log.Info("Transcription failed with an authentication-like error. Opening settings for API key update.");
                    ShowOverlay("API key issue detected — opening settings...", Color.Orange, 1800);
                    _transcriptionService = null;
                    OpenSettings(focusApiKey: true, restorePreviousFocus: false);
                }
            }
            finally
            {
                _isTranscribing = false;
                CompleteShutdownIfRequested();
            }
        }
        else
        {
            // Start recording
            try
            {
                _recorder.Start();
                _isRecording = true;
                StartListeningOverlay();
                _trayIcon.Icon = _appIcon;
                _trayIcon.Text = $"VoiceType - Recording... ({BuildHotkeyHint()} to stop)";
                Log.Info("Recording started");
            }
            catch (Exception ex)
            {
                _isRecording = false;
                StopListeningOverlay();
                Log.Error("Failed to start recording", ex);
                ShowOverlay("Microphone error: " + ex.Message, Color.Salmon, 4000);
                CompleteShutdownIfRequested();
            }
        }
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        OpenSettings();
    }

    private void OpenSettings(bool focusApiKey = false, bool restorePreviousFocus = true)
    {
        if (_overlay.InvokeRequired)
        {
            _overlay.Invoke(new Action(() => OpenSettings(focusApiKey, restorePreviousFocus)));
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
                if (focusApiKey)
                    dlg.FocusApiKeyInput();
                dlg.TopMost = false;
            }));
        };
        dlg.ShowDialog();
        LoadTranscriptionService();
        RefreshHotkeyRegistration();
        SetReadyState();
        if (restorePreviousFocus)
            RestorePreviousFocus(previousForegroundWindow, settingsWindow);
    }

    private void OnExit(object? sender, EventArgs e) => RequestShutdown();

    public void RequestShutdown()
    {
        if (_overlay.IsDisposed)
            return;

        Invoke(() =>
        {
            if (_isShuttingDown || _shutdownRequested)
                return;

            _shutdownRequested = true;
            Log.Info("Shutdown requested");

            if (_isRecording)
            {
                ShowOverlay("Close requested — finishing current recording...", Color.Gold, 0);
                OnHotkeyPressed(this, new HotkeyPressedEventArgs(PRIMARY_HOTKEY_ID));
                return;
            }

            if (_isTranscribing)
            {
                ShowOverlay("Close requested — finishing transcription...", Color.Gold, 0);
                return;
            }

            Shutdown();
        });
    }

    public void RequestListen()
    {
        if (_overlay.IsDisposed)
            return;

        Invoke(() =>
        {
            if (_shutdownRequested || _isShuttingDown)
            {
                Log.Info("Ignoring remote listen because shutdown is pending.");
                return;
            }

            if (TryCancelPendingPastePreview("remote listen request"))
                return;

            if (IsListenSuppressed("remote listen request"))
                return;

            Log.Info("Remote listen requested");
            OnHotkeyPressed(this, new HotkeyPressedEventArgs(PRIMARY_HOTKEY_ID));
        });
    }

    public void RequestSubmit()
    {
        if (_overlay.IsDisposed)
            return;

        Invoke(() =>
        {
            if (_shutdownRequested || _isShuttingDown)
            {
                Log.Info("Ignoring remote submit because shutdown is pending.");
                return;
            }

            Log.Info("Remote submit requested");
            TriggerSend();
        });
    }

    private void Shutdown()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        _shutdownRequested = true;
        StopListeningOverlay();
        EnsureTrayIconHidden();
        Application.Exit();
    }

    private void CompleteShutdownIfRequested()
    {
        if (_shutdownRequested)
        {
            if (!_isRecording && !_isTranscribing)
                Shutdown();
            return;
        }

        SetReadyState();
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

    private static string BuildOverlayHotkeyHint()
    {
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

    private int ShowOverlay(
        string text,
        Color? color = null,
        int? durationMs = null,
        ContentAlignment textAlign = ContentAlignment.MiddleCenter,
        bool centerTextBlock = false,
        bool showCountdownBar = false,
        bool tapToCancel = false)
    {
        if (!_enableOverlayPopups)
            return 0;

        var effectiveDurationMs = durationMs.HasValue
            ? (durationMs.Value <= 0 ? 0 : AppConfig.NormalizeOverlayDuration(durationMs.Value))
            : _overlayDurationMs;

        return _overlay.ShowMessage(
            text,
            color,
            effectiveDurationMs,
            textAlign,
            centerTextBlock,
            showCountdownBar,
            tapToCancel);
    }

    private int GetAdaptiveTranscribedOverlayDurationMs(string displayedText)
    {
        if (string.IsNullOrWhiteSpace(displayedText))
            return _overlayDurationMs;

        var words = displayedText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
        var adaptiveMs = AdaptiveOverlayBaseMs + (words * AdaptiveOverlayMsPerWord);
        var maxMs = Math.Min(AppConfig.MaxOverlayDurationMs, AdaptiveOverlayMaxMs);
        var preferredMs = Math.Max(_overlayDurationMs, adaptiveMs);
        return Math.Clamp(preferredMs, AppConfig.MinOverlayDurationMs, maxMs);
    }

    private void StartListeningOverlay()
    {
        if (!_enableOverlayPopups)
            return;

        _recordingStartedAtUtc = DateTime.UtcNow;
        Interlocked.Exchange(ref _micLevelPercent, 0);
        _micSpinnerIndex = 0;
        UpdateListeningOverlay();
        _listeningOverlayTimer.Start();
    }

    private void StopListeningOverlay()
    {
        _listeningOverlayTimer.Stop();
        Interlocked.Exchange(ref _micLevelPercent, 0);
    }

    private void OnRecorderInputLevelChanged(int levelPercent)
    {
        Interlocked.Exchange(ref _micLevelPercent, Math.Clamp(levelPercent, 0, 100));
    }

    private void UpdateListeningOverlay()
    {
        if (!_isRecording || !_enableOverlayPopups)
            return;

        var levelPercent = Interlocked.CompareExchange(ref _micLevelPercent, 0, 0);
        var elapsed = DateTime.UtcNow - _recordingStartedAtUtc;
        var elapsedText = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");

        if (_useSimpleMicSpinner)
        {
            var frame = MicSpinnerFrames[_micSpinnerIndex];
            _micSpinnerIndex = (_micSpinnerIndex + 1) % MicSpinnerFrames.Length;

            ShowOverlay(
                $"Listening {frame} {elapsedText}\nPress {BuildOverlayHotkeyHint()} to stop",
                Color.CornflowerBlue,
                0);
            return;
        }

        var meter = BuildMicActivityMeter(levelPercent, 18);
        ShowOverlay(
            $"Listening... {elapsedText}\nMic {meter} {levelPercent,3}%\nPress {BuildOverlayHotkeyHint()} to stop",
            Color.CornflowerBlue,
            0);
    }

    private static string BuildMicActivityMeter(int levelPercent, int segments)
    {
        var clampedLevel = Math.Clamp(levelPercent, 0, 100);
        var clampedSegments = Math.Max(6, segments);
        var filled = (int)Math.Round((clampedLevel / 100.0) * clampedSegments);
        if (filled > clampedSegments)
            filled = clampedSegments;

        return "[" + new string('|', filled) + new string('.', clampedSegments - filled) + "]";
    }

    private string? ParseVoiceCommand(string text)
    {
        return VoiceCommandParser.Parse(
            text,
            _enableOpenSettingsVoiceCommand,
            _enableExitAppVoiceCommand,
            _enableToggleAutoEnterVoiceCommand,
            _enableSendVoiceCommand,
            _enableShowVoiceCommandsVoiceCommand);
    }

    private void HandleVoiceCommand(string command)
    {
        switch (command)
        {
            case VoiceCommandParser.Exit:
                ShowOverlay("Command: exit app", CommandOverlayColor, 1000);
                _ = Task.Delay(800).ContinueWith(_ => Invoke(Shutdown));
                break;
            case VoiceCommandParser.Settings:
                ShowOverlay("Command: open settings", CommandOverlayColor, 1000);
                OnSettings(null, EventArgs.Empty);
                break;
            case VoiceCommandParser.AutoSendYes:
                SetAutoSend(true, fromVoiceCommand: true);
                break;
            case VoiceCommandParser.AutoSendNo:
                SetAutoSend(false, fromVoiceCommand: true);
                break;
            case VoiceCommandParser.Send:
                TriggerSend(fromVoiceCommand: true);
                break;
            case VoiceCommandParser.ShowVoiceCommands:
                ShowVoiceCommands(fromVoiceCommand: true);
                break;
        }
    }

    private void ShowVoiceCommands(bool fromVoiceCommand = false)
    {
        var commandStates = new (string Phrase, bool Enabled)[]
        {
            ("open settings", _enableOpenSettingsVoiceCommand),
            ("exit app", _enableExitAppVoiceCommand),
            ("auto-send on / auto-send off", _enableToggleAutoEnterVoiceCommand),
            ("submit", _enableSendVoiceCommand),
            ("show voice commands", _enableShowVoiceCommandsVoiceCommand)
        };

        var lines = new List<string>(commandStates.Length);
        var enabledCount = 0;
        var phraseColumnWidth = commandStates.Max(x => x.Phrase.Length);
        foreach (var (phrase, enabled) in commandStates)
        {
            if (enabled)
                enabledCount++;

            var statusTag = enabled ? "[ON ]" : "[OFF]";
            lines.Add($"{phrase.PadRight(phraseColumnWidth)}  {statusTag}");
        }

        var suffix = enabledCount == 0 ? "\nAll commands are disabled in Settings." : string.Empty;
        ShowOverlay(
            "Voice commands\n- " + string.Join("\n- ", lines) + suffix,
            enabledCount == 0
                ? Color.Gray
                : (fromVoiceCommand ? CommandOverlayColor : Color.CornflowerBlue),
            5500,
            ContentAlignment.TopLeft,
            centerTextBlock: true);
    }

    private void TriggerSend(bool fromVoiceCommand = false)
    {
        if (!TextInjector.SendEnter())
        {
            var failedText = fromVoiceCommand
                ? "Command: submit (no target window)"
                : "No target window to send Enter";
            ShowOverlay(failedText, Color.Gold, 1800);
            return;
        }

        var sentText = fromVoiceCommand ? "Command: submit" : "Submitted";
        var sentColor = fromVoiceCommand ? CommandOverlayColor : Color.LightGreen;
        ShowOverlay(sentText, sentColor, 1000);
        Log.Info("Enter key sent");
    }

    private void SetAutoSend(bool enabled, bool fromVoiceCommand = false)
    {
        if (_autoEnter == enabled)
        {
            var existingStateLabel = enabled ? "yes" : "no";
            var existingText = fromVoiceCommand
                ? $"Command: auto-send {existingStateLabel} (already set)"
                : $"Auto-send already {existingStateLabel}";
            var existingColor = fromVoiceCommand ? CommandOverlayColor : Color.Gray;
            ShowOverlay(existingText, existingColor, 1200);
            return;
        }

        try
        {
            var config = AppConfig.Load();
            config.AutoEnter = enabled;
            config.Save();
            _autoEnter = enabled;

            var stateLabel = enabled ? "yes" : "no";
            var stateText = fromVoiceCommand
                ? $"Command: auto-send {stateLabel}"
                : $"Auto-send {stateLabel}";
            var stateColor = fromVoiceCommand ? CommandOverlayColor : Color.LightGreen;
            ShowOverlay(stateText, stateColor, 1500);
            Log.Info($"Auto-send set via voice command ({stateLabel})");
        }
        catch (Exception ex)
        {
            Log.Error("Failed to update auto-send setting via voice command", ex);
            ShowOverlay("Failed to update auto-send setting", Color.Salmon, 2000);
        }
    }

    private void PromptForApiKeySetupOnStartup()
    {
        if (_promptedForApiKeyOnStartup)
            return;

        _promptedForApiKeyOnStartup = true;
        Log.Info("Opening settings on startup because API key is missing.");
        OpenSettings(focusApiKey: true, restorePreviousFocus: false);
    }

    private static bool IsLikelyApiKeyError(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
                continue;

            if (message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("incorrect api key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("authentication", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("403", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
            _listeningOverlayTimer.Stop();
            _listeningOverlayTimer.Dispose();
            _overlay.OverlayTapped -= OnOverlayTapped;
            _recorder.InputLevelChanged -= OnRecorderInputLevelChanged;
            _trayIcon.Dispose();
            _hotkeyWindow.Dispose();
            _overlay.Dispose();
            _recorder.Dispose();
            _appIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    private async Task<bool> ShowCancelableTranscribedPreviewAsync(string text, int durationMs)
    {
        var previewText = text;
        var messageId = ShowOverlay(
            previewText,
            Color.LightGreen,
            durationMs,
            showCountdownBar: true,
            tapToCancel: true);
        if (messageId == 0 || durationMs <= 0)
            return false;

        _pendingPastePreviewMessageId = messageId;
        _pendingPasteCanceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var waitMs = durationMs + TranscribedOverlayCancelWindowPaddingMs;
            var completed = await Task.WhenAny(_pendingPasteCanceledTcs.Task, Task.Delay(waitMs));
            if (completed == _pendingPasteCanceledTcs.Task)
                return _pendingPasteCanceledTcs.Task.Result;

            return false;
        }
        finally
        {
            _pendingPastePreviewMessageId = 0;
            _pendingPasteCanceledTcs = null;
        }
    }

    private void OnOverlayTapped(object? sender, OverlayTappedEventArgs e)
    {
        if (_pendingPastePreviewMessageId == 0 || e.MessageId != _pendingPastePreviewMessageId)
            return;

        _ = TryCancelPendingPastePreview("overlay tap");
    }

    private bool TryCancelPendingPastePreview(string source)
    {
        if (_pendingPastePreviewMessageId == 0 || _pendingPasteCanceledTcs == null)
            return false;

        if (_pendingPasteCanceledTcs.Task.IsCompleted)
            return false;

        var canceled = _pendingPasteCanceledTcs.TrySetResult(true);
        if (canceled)
        {
            ArmListenSuppression();
            Log.Info($"Pending paste preview canceled via {source}.");
        }
        return canceled;
    }

    private void ArmListenSuppression()
    {
        _ignoreListenUntilUtc = DateTime.UtcNow.AddMilliseconds(_cancelListenSuppressionMs);
    }

    private bool IsListenSuppressed(string source)
    {
        if (DateTime.UtcNow >= _ignoreListenUntilUtc)
            return false;

        Log.Info($"Ignoring {source} during post-cancel suppression.");
        return true;
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

