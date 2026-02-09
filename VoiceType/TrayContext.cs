using System.Runtime.InteropServices;

namespace VoiceType;

public class TrayContext : ApplicationContext
{
    private const int HOTKEY_ID = 1;
    private const int MOD_CTRL = 0x0002;
    private const int MOD_SHIFT = 0x0004;
    private const int VK_SPACE = 0x20;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly NotifyIcon _trayIcon;
    private readonly HotkeyWindow _hotkeyWindow;
    private readonly AudioRecorder _recorder;
    private readonly OverlayForm _overlay;
    private TranscriptionService? _transcriptionService;
    private bool _autoEnter;
    private bool _isRecording;
    private bool _isTranscribing;

    public TrayContext()
    {
        Log.Info("VoiceType starting...");

        _recorder = new AudioRecorder();
        _overlay = new OverlayForm();
        LoadTranscriptionService();

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "VoiceType - Ctrl+Shift+Space to dictate",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += OnHotkeyPressed;

        if (!RegisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID, MOD_CTRL | MOD_SHIFT, VK_SPACE))
        {
            Log.Error("Failed to register hotkey Ctrl+Shift+Space");
            MessageBox.Show(
                "Failed to register hotkey Ctrl+Shift+Space.\nAnother app may be using it.",
                "VoiceType", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            Log.Info("Hotkey registered: Ctrl+Shift+Space");
        }

        if (_transcriptionService == null)
        {
            Log.Info("No API key configured");
            _overlay.ShowMessage("No API key — right-click tray icon > Settings", Color.Orange, 5000);
        }
        else
        {
            _overlay.ShowMessage("VoiceType ready — Ctrl+Shift+Space to dictate", Color.LightGreen, 2000);
        }

        Log.Info("VoiceType started successfully");
    }

    private void LoadTranscriptionService()
    {
        var config = AppConfig.Load();
        _autoEnter = config.AutoEnter;
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
            _transcriptionService = new TranscriptionService(config.ApiKey, config.Model);
        else
            _transcriptionService = null;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings...", null, OnSettings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, OnExit);
        return menu;
    }

    private async void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_isTranscribing)
        {
            _overlay.ShowMessage("Still processing previous dictation...", Color.CornflowerBlue, 2000);
            return;
        }

        if (_transcriptionService == null)
        {
            _overlay.ShowMessage("No API key configured — check Settings", Color.Orange);
            return;
        }

        if (_isRecording)
        {
            // Stop recording and transcribe
            _isRecording = false;
            _trayIcon.Icon = SystemIcons.Information;
            _trayIcon.Text = "VoiceType - Transcribing...";
            _overlay.ShowMessage("Processing voice...", Color.CornflowerBlue, 10000);
            Log.Info("Recording stopped, starting transcription...");
            _isTranscribing = true;

            try
            {
                var audioData = _recorder.Stop();
                Log.Info($"Audio captured: {audioData.Length:N0} bytes");

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
                        _overlay.ShowMessage(text, Color.LightGreen, 4000);
                    }
                    else
                    {
                        Log.Info("No paste target, text on clipboard");
                        _overlay.ShowMessage(text + "\n(copied to clipboard — Ctrl+V to paste)", Color.Gold, 5000);
                    }
                }
                else
                {
                    _overlay.ShowMessage("No speech detected", Color.Gray, 2000);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Transcription failed", ex);
                _overlay.ShowMessage("Error: " + ex.Message, Color.Salmon, 4000);
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
                _trayIcon.Icon = SystemIcons.Exclamation;
                _trayIcon.Text = "VoiceType - Recording... (Ctrl+Shift+Space to stop)";
                _overlay.ShowMessage("Listening... speak now!", Color.CornflowerBlue, 30000);
                Log.Info("Recording started");
            }
            catch (Exception ex)
            {
                _isRecording = false;
                Log.Error("Failed to start recording", ex);
                _overlay.ShowMessage("Microphone error: " + ex.Message, Color.Salmon, 4000);
                SetReadyState();
            }
        }
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        using var dlg = new SettingsForm();
        dlg.ShowDialog();
        LoadTranscriptionService();
    }

    private void OnExit(object? sender, EventArgs e) => Shutdown();

    private void Shutdown()
    {
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void SetReadyState()
    {
        _trayIcon.Icon = SystemIcons.Application;
        _trayIcon.Text = "VoiceType - Ready (Ctrl+Shift+Space)";
    }

    private static string? ParseVoiceCommand(string text)
    {
        var normalized = text.Trim().TrimEnd('.', '!', ',', '?').ToLowerInvariant();
        return normalized switch
        {
            "exit me" or "goodbye" or
            "exit app" or "exit voice type" or "exit voicetype" or
            "close app" or "close voice type" or "close voicetype" or
            "quit app" or "quit voice type" or "quit voicetype"
                => "exit",

            "open settings" or "settings" or "open preferences" or "preferences"
                => "settings",

            _ => null
        };
    }

    private void HandleVoiceCommand(string command)
    {
        switch (command)
        {
            case "exit":
                _overlay.ShowMessage("Goodbye!", Color.LightGreen, 1000);
                _ = Task.Delay(800).ContinueWith(_ => Invoke(Shutdown));
                break;
            case "settings":
                _overlay.ShowMessage("Opening settings...", Color.CornflowerBlue, 1000);
                OnSettings(null, EventArgs.Empty);
                break;
        }
    }

    private void Invoke(Action action)
    {
        if (_overlay.InvokeRequired)
            _overlay.Invoke(action);
        else
            action();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnregisterHotKey(_hotkeyWindow.Handle, HOTKEY_ID);
            _trayIcon.Dispose();
            _hotkeyWindow.Dispose();
            _overlay.Dispose();
            _recorder.Dispose();
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
    public event EventHandler? HotkeyPressed;

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
