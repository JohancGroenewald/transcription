using System.ComponentModel;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace VoiceType;

public class SettingsForm : Form
{
    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_LAUNCH_APP1 = 17;
    private const int APPCOMMAND_LAUNCH_APP2 = 18;
    private const int WM_CTLCOLORLISTBOX = 0x0134;

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr hdc, int crColor);

    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(IntPtr hdc, int crColor);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int crColor);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly record struct SettingsTheme(
        bool IsDark,
        Color WindowBack,
        Color PanelBack,
        Color InputBack,
        Color ReadOnlyInputBack,
        Color ButtonBack,
        Color Border,
        Color Text,
        Color MutedText);

    private static readonly SettingsTheme LightTheme = new(
        IsDark: false,
        WindowBack: SystemColors.Control,
        PanelBack: SystemColors.Control,
        InputBack: SystemColors.Window,
        ReadOnlyInputBack: SystemColors.Control,
        ButtonBack: SystemColors.Control,
        Border: SystemColors.ActiveBorder,
        Text: SystemColors.ControlText,
        MutedText: Color.DimGray);

    private static readonly SettingsTheme DarkTheme = new(
        IsDark: true,
        WindowBack: Color.FromArgb(28, 28, 32),
        PanelBack: Color.FromArgb(34, 34, 40),
        InputBack: Color.FromArgb(50, 50, 58),
        ReadOnlyInputBack: Color.FromArgb(42, 42, 50),
        ButtonBack: Color.FromArgb(58, 58, 66),
        Border: Color.FromArgb(90, 90, 102),
        Text: Color.FromArgb(235, 235, 235),
        MutedText: Color.FromArgb(170, 170, 170));

    private readonly TextBox _apiKeyBox;
    private readonly ComboBox _modelBox;
    private readonly CheckBox _showKeyCheck;
    private readonly CheckBox _autoEnterCheck;
    private readonly CheckBox _debugLoggingCheck;
    private readonly CheckBox _enableOverlayPopupsCheck;
    private readonly NumericUpDown _overlayDurationMsInput;
    private readonly Label _overlayOpacityLabel;
    private readonly NumericUpDown _overlayOpacityInput;
    private readonly Label _overlayWidthLabel;
    private readonly NumericUpDown _overlayWidthInput;
    private readonly Label _overlayFontSizeLabel;
    private readonly NumericUpDown _overlayFontSizeInput;
    private readonly Label _overlayFadeProfileLabel;
    private readonly ComboBox _overlayFadeProfileCombo;
    private record struct AudioDeviceOption(int DeviceIndex, string DeviceName, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly ComboBox _microphoneDeviceCombo;
    private readonly Label _microphoneDeviceLabel;
    private readonly ComboBox _audioOutputDeviceCombo;
    private readonly Label _audioOutputDeviceLabel;
    private readonly CheckBox _showOverlayBorderCheck;
    private readonly CheckBox _useSimpleMicSpinnerCheck;
    private readonly CheckBox _enablePreviewPlaybackCleanupCheck;
    private readonly ComboBox _remoteActionPopupLevelCombo;
    private readonly CheckBox _enablePastedTextPrefixCheck;
    private readonly TextBox _pastedTextPrefixTextBox;
    private readonly CheckBox _enableTranscriptionPromptCheck;
    private readonly TextBox _transcriptionPromptTextBox;
    private readonly CheckBox _settingsDarkModeCheck;
    private readonly CheckBox _enablePenHotkeyCheck;
    private readonly ComboBox _penHotkeyBox;
    private readonly Label _penHotkeyLabel;
    private readonly Label _penHotkeyValidationResult;
    private readonly CheckBox _openSettingsVoiceCommandCheck;
    private readonly CheckBox _exitAppVoiceCommandCheck;
    private readonly CheckBox _toggleAutoEnterVoiceCommandCheck;
    private readonly CheckBox _sendVoiceCommandCheck;
    private readonly CheckBox _showVoiceCommandsVoiceCommandCheck;
    private readonly TextBox _voiceCommandValidationInput;
    private readonly Label _voiceCommandValidationResult;
    private readonly Button _voiceCommandsToggle;
    private readonly TableLayoutPanel _voiceCommandsBody;
    private bool _voiceCommandsExpanded = true;
    private readonly Label _versionValueLabel;
    private readonly Label _startedAtValueLabel;
    private readonly Label _uptimeValueLabel;
    private readonly System.Windows.Forms.Timer _uptimeTimer;
    private readonly Icon _formIcon;
    private bool _enablePreviewPlayback = true;
    private bool _settingsDarkModeEnabled;
    private IntPtr _comboListBackBrush;

    private sealed class ThemedGroupBox : GroupBox
    {
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = SystemColors.ActiveBorder;

        public ThemedGroupBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);

            var text = Text;
            var hasText = !string.IsNullOrWhiteSpace(text);

            var textSize = hasText
                ? TextRenderer.MeasureText(g, text, Font, Size.Empty, TextFormatFlags.SingleLine)
                : Size.Empty;

            var textLeft = 10;
            var textRect = hasText
                ? new Rectangle(textLeft, 0, textSize.Width, textSize.Height)
                : Rectangle.Empty;

            var borderTop = hasText ? (textRect.Height / 2) : 0;
            var borderRect = new Rectangle(0, borderTop, Width - 1, Height - borderTop - 1);

            using (var borderPen = new Pen(BorderColor))
                g.DrawRectangle(borderPen, borderRect);

            if (hasText)
            {
                // Mask out the border behind the caption text so the line doesn't run through the title.
                using (var backBrush = new SolidBrush(BackColor))
                    g.FillRectangle(backBrush, new Rectangle(textRect.Left - 2, textRect.Top, textRect.Width + 4, textRect.Height));

                TextRenderer.DrawText(
                    g,
                    text,
                    Font,
                    textRect,
                    ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    public SettingsForm()
    {
        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _formIcon = extractedIcon != null
            ? (Icon)extractedIcon.Clone()
            : (Icon)SystemIcons.Application.Clone();
        extractedIcon?.Dispose();

        Text = "VoiceType Settings";
        Icon = _formIcon;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        KeyPreview = true;
        Padding = new Padding(12);
        MinimumSize = new Size(760, 520);
        KeyDown += OnSettingsKeyDown;

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var contentPanel = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = false,
            Margin = new Padding(0, 0, 0, 8)
        };

        var contentLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var grpApi = new ThemedGroupBox
        {
            Text = "OpenAI API",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var apiLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        apiLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        apiLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        apiLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        apiLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        apiLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblKeyHelp = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Text = "Stored encrypted for this Windows user account.",
            Margin = new Padding(0, 0, 0, 8)
        };

        var lblKey = new Label
        {
            Text = "API key",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 8, 3)
        };

        _apiKeyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            Margin = new Padding(0, 0, 8, 0),
            PlaceholderText = "sk-...",
            MinimumSize = new Size(340, 0)
        };

        _showKeyCheck = new CheckBox
        {
            Text = "Show",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _showKeyCheck.CheckedChanged += (_, _) =>
            _apiKeyBox.UseSystemPasswordChar = !_showKeyCheck.Checked;

        apiLayout.Controls.Add(lblKeyHelp, 0, 0);
        apiLayout.SetColumnSpan(lblKeyHelp, 3);
        apiLayout.Controls.Add(lblKey, 0, 1);
        apiLayout.Controls.Add(_apiKeyBox, 1, 1);
        apiLayout.Controls.Add(_showKeyCheck, 2, 1);
        grpApi.Controls.Add(apiLayout);

        var grpBehavior = new ThemedGroupBox
        {
            Text = "Behavior",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var behaviorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 24,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        behaviorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        behaviorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (var i = 0; i < behaviorLayout.RowCount; i++)
            behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblModel = new Label
        {
            Text = "Transcription model",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 10, 3)
        };

        _modelBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 0, 0, 4),
            MinimumSize = new Size(260, 0)
        };
        _modelBox.Items.AddRange(["whisper-1", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"]);
        SetupThemedComboBox(_modelBox);

        _autoEnterCheck = new CheckBox
        {
            Text = "Press Enter after pasting text",
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };

        _debugLoggingCheck = new CheckBox
        {
            Text = "Enable file logging (debug only)",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };

        _enableOverlayPopupsCheck = new CheckBox
        {
            Text = "Show popup notifications",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };
        _enableOverlayPopupsCheck.CheckedChanged += (_, _) => UpdateOverlaySettingsState();

        var lblOverlayDuration = new Label
        {
            Text = "Popup duration (ms)",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _overlayDurationMsInput = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = AppConfig.MinOverlayDurationMs,
            Maximum = AppConfig.MaxOverlayDurationMs,
            Increment = 250,
            ThousandsSeparator = true,
            Margin = new Padding(0, 6, 0, 0)
        };

        _overlayOpacityLabel = new Label
        {
            Text = "HUD opacity (%)",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _overlayOpacityInput = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = AppConfig.MinOverlayOpacityPercent,
            Maximum = AppConfig.MaxOverlayOpacityPercent,
            Increment = 1,
            ThousandsSeparator = false,
            Margin = new Padding(0, 6, 0, 0)
        };

        _overlayWidthLabel = new Label
        {
            Text = "HUD width (%)",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _overlayWidthInput = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = AppConfig.MinOverlayWidthPercent,
            Maximum = AppConfig.MaxOverlayWidthPercent,
            Increment = 1,
            ThousandsSeparator = false,
            Margin = new Padding(0, 6, 0, 0)
        };

        _overlayFontSizeLabel = new Label
        {
            Text = "HUD font size (pt)",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _overlayFontSizeInput = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 120,
            Minimum = AppConfig.MinOverlayFontSizePt,
            Maximum = AppConfig.MaxOverlayFontSizePt,
            Increment = 1,
            ThousandsSeparator = false,
            Margin = new Padding(0, 6, 0, 0)
        };

        _overlayFadeProfileLabel = new Label
        {
            Text = "HUD fade profile",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _overlayFadeProfileCombo = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 240,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 6, 0, 0)
        };
        _overlayFadeProfileCombo.Items.AddRange([.. AppConfig.OverlayFadeProfiles]);
        SetupThemedComboBox(_overlayFadeProfileCombo);

        _showOverlayBorderCheck = new CheckBox
        {
            Text = "Show HUD border line",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        _useSimpleMicSpinnerCheck = new CheckBox
        {
            Text = "Use simple mic spinner (instead of level meter)",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        _enablePreviewPlaybackCleanupCheck = new CheckBox
        {
            Text = "Enable preview playback cleanup pass",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        _microphoneDeviceLabel = new Label
        {
            Text = "Microphone input",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };
        _microphoneDeviceCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 440,
            Margin = new Padding(0, 6, 0, 0)
        };
        PopulateAudioDeviceCombo(_microphoneDeviceCombo, GetMicrophoneDeviceOptions());
        SetupThemedComboBox(_microphoneDeviceCombo);

        _audioOutputDeviceLabel = new Label
        {
            Text = "Audio output",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };
        _audioOutputDeviceCombo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 440,
            Margin = new Padding(0, 6, 0, 0)
        };
        PopulateAudioDeviceCombo(_audioOutputDeviceCombo, GetOutputDeviceOptions());
        SetupThemedComboBox(_audioOutputDeviceCombo);

        var lblRemoteActionPopupLevel = new Label
        {
            Text = "Remote action pop-up level",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _remoteActionPopupLevelCombo = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 240,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 6, 0, 0)
        };
        _remoteActionPopupLevelCombo.Items.AddRange(new object[]
        {
            "Off",
            "Basic",
            "Detailed"
        });
        SetupThemedComboBox(_remoteActionPopupLevelCombo);

        var lblPastedTextPrefix = new Label
        {
            Text = "Pasted text prefix",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _enablePastedTextPrefixCheck = new CheckBox
        {
            Text = "Enable pasted text prefix",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        _enablePastedTextPrefixCheck.CheckedChanged += (_, _) => UpdatePastedTextPrefixState();

        const int PrefixEditorLineCount = 5;
        var prefixEditorMinHeight = (PrefixEditorLineCount * Font.Height) + 8;

        _pastedTextPrefixTextBox = new TextBox
         {
             Dock = DockStyle.Fill,
             Margin = new Padding(0, 4, 0, 0),
            AutoSize = false,
            Height = prefixEditorMinHeight,
            MinimumSize = new Size(0, prefixEditorMinHeight),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
             PlaceholderText = "Optional prefix"
         };

        _enableTranscriptionPromptCheck = new CheckBox
        {
            Text = "Enable custom transcription prompt",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        _enableTranscriptionPromptCheck.CheckedChanged += (_, _) => UpdateTranscriptionPromptState();

        const int TranscriptionPromptEditorLineCount = 6;
        var transcriptionPromptEditorMinHeight = (TranscriptionPromptEditorLineCount * Font.Height) + 8;
        _transcriptionPromptTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 0, 0),
            AutoSize = false,
            Height = transcriptionPromptEditorMinHeight,
            MinimumSize = new Size(0, transcriptionPromptEditorMinHeight),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
            PlaceholderText = "Optional transcription prompt"
        };

        _settingsDarkModeCheck = new CheckBox
        {
            Text = "Dark mode (settings window)",
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        _settingsDarkModeCheck.CheckedChanged += (_, _) => ApplySettingsTheme(_settingsDarkModeCheck.Checked);

        _enablePenHotkeyCheck = new CheckBox
        {
            Text = "Enable Surface Pen hotkey",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        _enablePenHotkeyCheck.CheckedChanged += (_, _) => UpdatePenHotkeySettingsState();

        _penHotkeyLabel = new Label
        {
            Text = "Surface Pen key",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 10, 3)
        };

        _penHotkeyBox = new ComboBox
        {
            Dock = DockStyle.Left,
            Width = 160,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, 6, 0, 0)
        };
        _penHotkeyBox.Items.AddRange(AppConfig.GetSupportedPenHotkeys().Cast<object>().ToArray());
        SetupThemedComboBox(_penHotkeyBox);

        var penValidationLabel = new Label
        {
            Text = "Pen button validator",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 2)
        };

        _penHotkeyValidationResult = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 2, 0, 0),
            MaximumSize = new Size(520, 0),
            Text = "Press a pen button while this window is focused."
        };

        behaviorLayout.Controls.Add(lblModel, 0, 0);
        behaviorLayout.Controls.Add(_modelBox, 1, 0);
        behaviorLayout.Controls.Add(_autoEnterCheck, 0, 1);
        behaviorLayout.SetColumnSpan(_autoEnterCheck, 2);
        behaviorLayout.Controls.Add(_debugLoggingCheck, 0, 2);
        behaviorLayout.SetColumnSpan(_debugLoggingCheck, 2);
        behaviorLayout.Controls.Add(_enableOverlayPopupsCheck, 0, 3);
        behaviorLayout.SetColumnSpan(_enableOverlayPopupsCheck, 2);
        behaviorLayout.Controls.Add(lblOverlayDuration, 0, 4);
        behaviorLayout.Controls.Add(_overlayDurationMsInput, 1, 4);
        behaviorLayout.Controls.Add(_overlayOpacityLabel, 0, 5);
        behaviorLayout.Controls.Add(_overlayOpacityInput, 1, 5);
        behaviorLayout.Controls.Add(_overlayWidthLabel, 0, 6);
        behaviorLayout.Controls.Add(_overlayWidthInput, 1, 6);
        behaviorLayout.Controls.Add(_overlayFontSizeLabel, 0, 7);
        behaviorLayout.Controls.Add(_overlayFontSizeInput, 1, 7);
        behaviorLayout.Controls.Add(_showOverlayBorderCheck, 0, 8);
        behaviorLayout.SetColumnSpan(_showOverlayBorderCheck, 2);
        behaviorLayout.Controls.Add(_useSimpleMicSpinnerCheck, 0, 9);
        behaviorLayout.SetColumnSpan(_useSimpleMicSpinnerCheck, 2);
        behaviorLayout.Controls.Add(_enablePreviewPlaybackCleanupCheck, 0, 10);
        behaviorLayout.SetColumnSpan(_enablePreviewPlaybackCleanupCheck, 2);
        behaviorLayout.Controls.Add(_microphoneDeviceLabel, 0, 22);
        behaviorLayout.Controls.Add(_microphoneDeviceCombo, 1, 22);
        behaviorLayout.Controls.Add(_audioOutputDeviceLabel, 0, 23);
        behaviorLayout.Controls.Add(_audioOutputDeviceCombo, 1, 23);
        behaviorLayout.Controls.Add(lblRemoteActionPopupLevel, 0, 11);
        behaviorLayout.Controls.Add(_remoteActionPopupLevelCombo, 1, 11);
        behaviorLayout.Controls.Add(_enablePastedTextPrefixCheck, 0, 12);
        behaviorLayout.SetColumnSpan(_enablePastedTextPrefixCheck, 2);
        behaviorLayout.Controls.Add(lblPastedTextPrefix, 0, 13);
        behaviorLayout.Controls.Add(_pastedTextPrefixTextBox, 1, 13);
        behaviorLayout.Controls.Add(_enableTranscriptionPromptCheck, 0, 19);
        behaviorLayout.SetColumnSpan(_enableTranscriptionPromptCheck, 2);
        behaviorLayout.Controls.Add(_transcriptionPromptTextBox, 0, 20);
        behaviorLayout.SetColumnSpan(_transcriptionPromptTextBox, 2);
        behaviorLayout.Controls.Add(_enablePenHotkeyCheck, 0, 14);
        behaviorLayout.SetColumnSpan(_enablePenHotkeyCheck, 2);
        behaviorLayout.Controls.Add(_penHotkeyLabel, 0, 15);
        behaviorLayout.Controls.Add(_penHotkeyBox, 1, 15);
        behaviorLayout.Controls.Add(penValidationLabel, 0, 16);
        behaviorLayout.SetColumnSpan(penValidationLabel, 2);
        behaviorLayout.Controls.Add(_penHotkeyValidationResult, 0, 17);
        behaviorLayout.SetColumnSpan(_penHotkeyValidationResult, 2);
        behaviorLayout.Controls.Add(_settingsDarkModeCheck, 0, 18);
        behaviorLayout.SetColumnSpan(_settingsDarkModeCheck, 2);
        behaviorLayout.Controls.Add(_overlayFadeProfileLabel, 0, 21);
        behaviorLayout.Controls.Add(_overlayFadeProfileCombo, 1, 21);
        grpBehavior.Controls.Add(behaviorLayout);

        var grpVoiceCommands = new ThemedGroupBox
        {
            Text = "Voice Commands",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 0)
        };

        var voiceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        voiceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _voiceCommandsToggle = new Button
        {
            Text = "▾ Voice Commands",
            AutoSize = false,
            Height = 24,
            Dock = DockStyle.Top,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Margin = new Padding(0, 0, 0, 8),
            FlatStyle = FlatStyle.Flat
        };
        _voiceCommandsToggle.Click += (_, _) => SetVoiceCommandsExpanded(!_voiceCommandsExpanded);
        _voiceCommandsToggle.FlatAppearance.BorderColor = SystemColors.ActiveBorder;
        _voiceCommandsToggle.FlatAppearance.BorderSize = 1;

        _voiceCommandsBody = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _voiceCommandsBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _voiceCommandsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _voiceCommandsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _voiceCommandsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _voiceCommandsBody.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var voiceHelp = new Label
        {
            Text = "Each command can be enabled/disabled. Common supported phrases are listed below each command.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8)
        };

        var voiceCommandTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
        for (var i = 0; i < 5; i++)
            voiceCommandTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
        voiceCommandTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceCommandTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _openSettingsVoiceCommandCheck = new CheckBox
        {
            Text = "Open Settings",
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 4)
        };
        _exitAppVoiceCommandCheck = new CheckBox
        {
            Text = "Exit App",
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 4)
        };
        _toggleAutoEnterVoiceCommandCheck = new CheckBox
        {
            Text = "Auto-Send",
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 4)
        };
        _sendVoiceCommandCheck = new CheckBox
        {
            Text = "Submit",
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 4)
        };
        _showVoiceCommandsVoiceCommandCheck = new CheckBox
        {
            Text = "Show Commands",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        var openSettingsExamples = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 10, 8),
            Text = "open settings\nopen settings screen\nshow settings\nshow settings screen"
        };
        var exitAppExamples = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 10, 8),
            Text = "exit app\nclose app\nquit app\nclose voice type"
        };
        var autoSendExamples = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 10, 8),
            Text = "auto-send on\nauto-send off\nauto on / auto off\nenable/disable auto-send"
        };
        var sendExamples = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 10, 8),
            Text = "submit\nsend\nsend message\nsend command\npress enter"
        };
        var showCommandsExamples = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8),
            Text = "show voice commands\nshow voice command\nlist voice commands\nwhat are voice commands"
        };

        voiceCommandTable.Controls.Add(_openSettingsVoiceCommandCheck, 0, 0);
        voiceCommandTable.Controls.Add(_exitAppVoiceCommandCheck, 1, 0);
        voiceCommandTable.Controls.Add(_toggleAutoEnterVoiceCommandCheck, 2, 0);
        voiceCommandTable.Controls.Add(_sendVoiceCommandCheck, 3, 0);
        voiceCommandTable.Controls.Add(_showVoiceCommandsVoiceCommandCheck, 4, 0);
        voiceCommandTable.Controls.Add(openSettingsExamples, 0, 1);
        voiceCommandTable.Controls.Add(exitAppExamples, 1, 1);
        voiceCommandTable.Controls.Add(autoSendExamples, 2, 1);
        voiceCommandTable.Controls.Add(sendExamples, 3, 1);
        voiceCommandTable.Controls.Add(showCommandsExamples, 4, 1);

        var validatorPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0)
        };
        validatorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        validatorPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        validatorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        validatorPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var validatorLabel = new Label
        {
            Text = "Validate phrase",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4),
            Font = new Font(Font, FontStyle.Bold)
        };

        _voiceCommandValidationInput = new TextBox
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 8, 0),
            PlaceholderText = "e.g. auto send off",
            MinimumSize = new Size(260, 0)
        };
        _voiceCommandValidationInput.TextChanged += (_, _) => ValidateVoiceCommandInput();
        _voiceCommandValidationInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                ValidateVoiceCommandInput();
            }
        };

        var validateButton = new Button
        {
            Text = "Validate",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        validateButton.Click += (_, _) => ValidateVoiceCommandInput();

        _voiceCommandValidationResult = new Label
        {
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 6, 0, 0),
            MaximumSize = new Size(520, 0),
            Text = "Type a phrase to test command recognition."
        };

        validatorPanel.Controls.Add(validatorLabel, 0, 0);
        validatorPanel.SetColumnSpan(validatorLabel, 2);
        validatorPanel.Controls.Add(_voiceCommandValidationInput, 0, 1);
        validatorPanel.Controls.Add(validateButton, 1, 1);

        _openSettingsVoiceCommandCheck.CheckedChanged += (_, _) => ValidateVoiceCommandInput();
        _exitAppVoiceCommandCheck.CheckedChanged += (_, _) => ValidateVoiceCommandInput();
        _toggleAutoEnterVoiceCommandCheck.CheckedChanged += (_, _) => ValidateVoiceCommandInput();
        _sendVoiceCommandCheck.CheckedChanged += (_, _) => ValidateVoiceCommandInput();
        _showVoiceCommandsVoiceCommandCheck.CheckedChanged += (_, _) => ValidateVoiceCommandInput();

        _voiceCommandsBody.Controls.Add(voiceHelp, 0, 0);
        _voiceCommandsBody.Controls.Add(voiceCommandTable, 0, 1);
        _voiceCommandsBody.Controls.Add(validatorPanel, 0, 2);
        _voiceCommandsBody.Controls.Add(_voiceCommandValidationResult, 0, 3);
        voiceLayout.Controls.Add(_voiceCommandsToggle, 0, 0);
        voiceLayout.Controls.Add(_voiceCommandsBody, 0, 1);
        SetVoiceCommandsExpanded(_voiceCommandsExpanded);
        grpVoiceCommands.Controls.Add(voiceLayout);

        var grpAppInfo = new ThemedGroupBox
        {
            Text = "App Info",
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 10, 0, 0)
        };

        var appInfoLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        appInfoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appInfoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        appInfoLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        appInfoLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        appInfoLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblVersion = new Label
        {
            Text = "Version",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 10, 3)
        };
        _versionValueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = AppInfo.Version
        };

        var lblStartedAt = new Label
        {
            Text = "Started",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 10, 3)
        };
        _startedAtValueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Text = AppInfo.StartedAtLocal.ToString("yyyy-MM-dd HH:mm:ss")
        };

        var lblUptime = new Label
        {
            Text = "Uptime",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 10, 3)
        };
        _uptimeValueLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        appInfoLayout.Controls.Add(lblVersion, 0, 0);
        appInfoLayout.Controls.Add(_versionValueLabel, 1, 0);
        appInfoLayout.Controls.Add(lblStartedAt, 0, 1);
        appInfoLayout.Controls.Add(_startedAtValueLabel, 1, 1);
        appInfoLayout.Controls.Add(lblUptime, 0, 2);
        appInfoLayout.Controls.Add(_uptimeValueLabel, 1, 2);
        grpAppInfo.Controls.Add(appInfoLayout);

        _uptimeTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
        _uptimeTimer.Tick += (_, _) => UpdateAppInfo();
        UpdateAppInfo();

        var btnExit = new Button
        {
            Text = "Exit VoiceType",
            AutoSize = true,
            MinimumSize = new Size(110, 32),
            Anchor = AnchorStyles.Left
        };
        btnExit.Click += (_, _) =>
        {
            Close();
            Application.Exit();
        };

        var btnSave = new Button
        {
            Text = "Save",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.OK
        };
        btnSave.Click += OnSave;

        var btnCancel = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.Cancel
        };

        var rightButtonsPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        };
        rightButtonsPanel.Controls.Add(btnSave);
        rightButtonsPanel.Controls.Add(btnCancel);

        var buttonsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0)
        };
        buttonsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        buttonsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        buttonsLayout.Controls.Add(btnExit, 0, 0);
        buttonsLayout.Controls.Add(rightButtonsPanel, 2, 0);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        contentLayout.Controls.Add(grpApi, 0, 0);
        contentLayout.Controls.Add(grpBehavior, 0, 1);
        contentLayout.Controls.Add(grpVoiceCommands, 0, 2);
        contentLayout.Controls.Add(grpAppInfo, 0, 3);
        contentPanel.Controls.Add(contentLayout);

        rootLayout.Controls.Add(contentPanel, 0, 0);
        rootLayout.Controls.Add(buttonsLayout, 0, 1);
        Controls.Add(rootLayout);

        var config = AppConfig.Load();
        _apiKeyBox.Text = config.ApiKey;
        _modelBox.SelectedItem = config.Model;
        if (_modelBox.SelectedIndex < 0)
            _modelBox.SelectedIndex = 0;

        _autoEnterCheck.Checked = config.AutoEnter;
        _debugLoggingCheck.Checked = config.EnableDebugLogging;
        _enableOverlayPopupsCheck.Checked = config.EnableOverlayPopups;
        _overlayDurationMsInput.Value = AppConfig.NormalizeOverlayDuration(config.OverlayDurationMs);
        _overlayOpacityInput.Value = AppConfig.NormalizeOverlayOpacityPercent(config.OverlayOpacityPercent);
        _overlayWidthInput.Value = AppConfig.NormalizeOverlayWidthPercent(config.OverlayWidthPercent);
        _overlayFontSizeInput.Value = AppConfig.NormalizeOverlayFontSizePt(config.OverlayFontSizePt);
        _overlayFadeProfileCombo.SelectedIndex = Math.Clamp(
            AppConfig.NormalizeOverlayFadeProfile(config.OverlayFadeProfile),
            0,
            _overlayFadeProfileCombo.Items.Count - 1);
        _showOverlayBorderCheck.Checked = config.ShowOverlayBorder;
        _useSimpleMicSpinnerCheck.Checked = config.UseSimpleMicSpinner;
        _enablePreviewPlaybackCleanupCheck.Checked = config.EnablePreviewPlaybackCleanup;
        _enablePreviewPlayback = config.EnablePreviewPlayback;
        SetSelectedAudioDevice(_microphoneDeviceCombo, config.MicrophoneInputDeviceIndex, config.MicrophoneInputDeviceName);
        SetSelectedAudioDevice(_audioOutputDeviceCombo, config.AudioOutputDeviceIndex, config.AudioOutputDeviceName);
        _remoteActionPopupLevelCombo.SelectedIndex = Math.Clamp(
            AppConfig.NormalizeRemoteActionPopupLevel(config.RemoteActionPopupLevel),
            0,
            _remoteActionPopupLevelCombo.Items.Count - 1);
        _enablePastedTextPrefixCheck.Checked = config.EnablePastedTextPrefix;
        _pastedTextPrefixTextBox.Text = config.PastedTextPrefix ?? "";
        _enableTranscriptionPromptCheck.Checked = config.EnableTranscriptionPrompt;
        _transcriptionPromptTextBox.Text = config.TranscriptionPrompt ?? "";
        _settingsDarkModeCheck.Checked = config.EnableSettingsDarkMode;
        _enablePenHotkeyCheck.Checked = config.EnablePenHotkey;
        _penHotkeyBox.SelectedItem = AppConfig.NormalizePenHotkey(config.PenHotkey);
        if (_penHotkeyBox.SelectedIndex < 0)
            _penHotkeyBox.SelectedItem = AppConfig.DefaultPenHotkey;
        _openSettingsVoiceCommandCheck.Checked = config.EnableOpenSettingsVoiceCommand;
        _exitAppVoiceCommandCheck.Checked = config.EnableExitAppVoiceCommand;
        _toggleAutoEnterVoiceCommandCheck.Checked = config.EnableToggleAutoEnterVoiceCommand;
        _sendVoiceCommandCheck.Checked = config.EnableSendVoiceCommand;
        _showVoiceCommandsVoiceCommandCheck.Checked = config.EnableShowVoiceCommandsVoiceCommand;
        UpdateOverlaySettingsState();
        UpdatePastedTextPrefixState();
        UpdateTranscriptionPromptState();
        UpdatePenHotkeySettingsState();
        ValidateVoiceCommandInput();
        WrapWindowToContent(contentPanel, buttonsLayout);
        Invalidate(true);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.None;

        var config = new AppConfig
        {
            ApiKey = _apiKeyBox.Text.Trim(),
            Model = _modelBox.SelectedItem?.ToString() ?? "whisper-1",
            AutoEnter = _autoEnterCheck.Checked,
            EnableDebugLogging = _debugLoggingCheck.Checked,
            EnableOverlayPopups = _enableOverlayPopupsCheck.Checked,
            OverlayDurationMs = AppConfig.NormalizeOverlayDuration((int)_overlayDurationMsInput.Value),
            OverlayOpacityPercent = AppConfig.NormalizeOverlayOpacityPercent((int)_overlayOpacityInput.Value),
            OverlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent((int)_overlayWidthInput.Value),
            OverlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt((int)_overlayFontSizeInput.Value),
            OverlayFadeProfile = Math.Clamp(
                _overlayFadeProfileCombo.SelectedIndex,
                AppConfig.MinOverlayFadeProfile,
                AppConfig.MaxOverlayFadeProfile),
            ShowOverlayBorder = _showOverlayBorderCheck.Checked,
            UseSimpleMicSpinner = _useSimpleMicSpinnerCheck.Checked,
            EnablePreviewPlaybackCleanup = _enablePreviewPlaybackCleanupCheck.Checked,
            EnablePreviewPlayback = _enablePreviewPlayback,
            MicrophoneInputDeviceIndex = GetSelectedAudioDeviceIndex(_microphoneDeviceCombo),
            MicrophoneInputDeviceName = GetSelectedAudioDeviceName(_microphoneDeviceCombo),
            AudioOutputDeviceIndex = GetSelectedAudioDeviceIndex(_audioOutputDeviceCombo),
            AudioOutputDeviceName = GetSelectedAudioDeviceName(_audioOutputDeviceCombo),
            RemoteActionPopupLevel = Math.Clamp(
                _remoteActionPopupLevelCombo.SelectedIndex,
                AppConfig.MinRemoteActionPopupLevel,
                AppConfig.MaxRemoteActionPopupLevel),
            EnablePastedTextPrefix = _enablePastedTextPrefixCheck.Checked,
            PastedTextPrefix = _pastedTextPrefixTextBox.Text,
            EnableTranscriptionPrompt = _enableTranscriptionPromptCheck.Checked,
            TranscriptionPrompt = _transcriptionPromptTextBox.Text,
            EnableSettingsDarkMode = _settingsDarkModeCheck.Checked,
            EnablePenHotkey = _enablePenHotkeyCheck.Checked,
            PenHotkey = AppConfig.NormalizePenHotkey(_penHotkeyBox.SelectedItem?.ToString()),
            EnableOpenSettingsVoiceCommand = _openSettingsVoiceCommandCheck.Checked,
            EnableExitAppVoiceCommand = _exitAppVoiceCommandCheck.Checked,
            EnableToggleAutoEnterVoiceCommand = _toggleAutoEnterVoiceCommandCheck.Checked,
            EnableSendVoiceCommand = _sendVoiceCommandCheck.Checked,
            EnableShowVoiceCommandsVoiceCommand = _showVoiceCommandsVoiceCommandCheck.Checked
        };

        try
        {
            config.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
            MessageBox.Show(
                "Failed to save settings. Please check file permissions and try again.",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static IReadOnlyList<AudioDeviceOption> GetMicrophoneDeviceOptions()
    {
        var options = new List<AudioDeviceOption>
        {
            new(AppConfig.DefaultAudioDeviceIndex, string.Empty, "System default (recommended)")
        };

        try
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                var deviceName = TryGetCaptureDeviceName(i);
                options.Add(new AudioDeviceOption(i, deviceName, $"[{i}] {deviceName}"));
            }
        }
        catch
        {
            // Keep settings usable if device enumeration fails.
        }

        return options;
    }

    private static IReadOnlyList<AudioDeviceOption> GetOutputDeviceOptions()
    {
        var options = new List<AudioDeviceOption>
        {
            new(AppConfig.DefaultAudioDeviceIndex, string.Empty, "System default (recommended)")
        };

        try
        {
            for (var i = 0; i < WaveOut.DeviceCount; i++)
            {
                var deviceName = TryGetWaveOutDeviceName(i);
                options.Add(new AudioDeviceOption(
                    i,
                    deviceName,
                    $"[{i}] {deviceName}"));
            }
        }
        catch
        {
            // Keep settings usable if device enumeration fails.
        }

        return options;
    }

    private static void PopulateAudioDeviceCombo(ComboBox combo, IReadOnlyList<AudioDeviceOption> deviceOptions)
    {
        combo.Items.Clear();
        foreach (var option in deviceOptions)
            combo.Items.Add(option);

        combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
    }

    private static void SetSelectedAudioDevice(
        ComboBox combo,
        int selectedDeviceIndex,
        string? selectedDeviceName)
    {
        foreach (AudioDeviceOption option in combo.Items)
        {
            if (option.DeviceIndex == selectedDeviceIndex)
            {
                combo.SelectedItem = option;
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedDeviceName))
        {
            foreach (AudioDeviceOption option in combo.Items)
            {
                if (string.Equals(option.DeviceName, selectedDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = option;
                    return;
                }
            }
        }

        combo.SelectedIndex = 0;
    }

    private static int GetSelectedAudioDeviceIndex(ComboBox combo)
    {
        return combo.SelectedItem is AudioDeviceOption option
            ? option.DeviceIndex
            : AppConfig.DefaultAudioDeviceIndex;
    }

    private static string GetSelectedAudioDeviceName(ComboBox combo)
    {
        return combo.SelectedItem is AudioDeviceOption option && option.DeviceIndex >= 0
            ? option.DeviceName
            : string.Empty;
    }

    private static string TryGetCaptureDeviceName(int deviceIndex)
    {
        try
        {
            return WaveIn.GetCapabilities(deviceIndex).ProductName;
        }
        catch
        {
            return $"Input {deviceIndex}";
        }
    }

    private static string TryGetWaveOutDeviceName(int deviceIndex)
    {
        try
        {
            return WaveOut.GetCapabilities(deviceIndex).ProductName;
        }
        catch
        {
            return $"Output {deviceIndex}";
        }
    }

    public void FocusApiKeyInput()
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(new Action(FocusApiKeyInput));
            return;
        }

        _apiKeyBox.Focus();
        _apiKeyBox.SelectionStart = 0;
        _apiKeyBox.SelectionLength = _apiKeyBox.TextLength;
    }

    private void ValidateVoiceCommandInput()
    {
        var candidate = _voiceCommandValidationInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            _voiceCommandValidationResult.ForeColor = GetMutedTextColor();
            _voiceCommandValidationResult.Text = "Type a phrase to test command recognition.";
            return;
        }

        var matchedEnabledCommand = VoiceCommandParser.Parse(
            candidate,
            _openSettingsVoiceCommandCheck.Checked,
            _exitAppVoiceCommandCheck.Checked,
            _toggleAutoEnterVoiceCommandCheck.Checked,
            _sendVoiceCommandCheck.Checked,
            _showVoiceCommandsVoiceCommandCheck.Checked);

        if (matchedEnabledCommand != null)
        {
            _voiceCommandValidationResult.ForeColor = Color.ForestGreen;
            _voiceCommandValidationResult.Text =
                $"Matches: {VoiceCommandParser.GetDisplayName(matchedEnabledCommand)} (enabled)";
            return;
        }

        var matchedAnyCommand = VoiceCommandParser.Parse(
            candidate,
            enableOpenSettingsVoiceCommand: true,
            enableExitAppVoiceCommand: true,
            enableToggleAutoEnterVoiceCommand: true,
            enableSendVoiceCommand: true,
            enableShowVoiceCommandsVoiceCommand: true);

        if (matchedAnyCommand != null)
        {
            _voiceCommandValidationResult.ForeColor = Color.DarkOrange;
            _voiceCommandValidationResult.Text =
                $"Matches: {VoiceCommandParser.GetDisplayName(matchedAnyCommand)} (currently disabled)";
            return;
        }

        _voiceCommandValidationResult.ForeColor = GetMutedTextColor();
        _voiceCommandValidationResult.Text = "No command match.";
    }

    private void UpdateOverlaySettingsState()
    {
        var enabled = _enableOverlayPopupsCheck.Checked;
        _overlayDurationMsInput.Enabled = enabled;
        _overlayOpacityInput.Enabled = enabled;
        _overlayWidthInput.Enabled = enabled;
        _overlayFontSizeInput.Enabled = enabled;
        _overlayFadeProfileCombo.Enabled = enabled;
        _showOverlayBorderCheck.Enabled = enabled;
        _useSimpleMicSpinnerCheck.Enabled = enabled;
        _enablePreviewPlaybackCleanupCheck.Enabled = enabled;

        // Labels do not render theme-aware when disabled; keep them enabled and adjust color instead.
        var theme = GetActiveTheme();
        var labelColor = enabled ? theme.Text : theme.MutedText;
        _overlayOpacityLabel.Enabled = true;
        _overlayOpacityLabel.ForeColor = labelColor;
        _overlayWidthLabel.Enabled = true;
        _overlayWidthLabel.ForeColor = labelColor;
        _overlayFontSizeLabel.Enabled = true;
        _overlayFontSizeLabel.ForeColor = labelColor;
        _overlayFadeProfileLabel.Enabled = true;
        _overlayFadeProfileLabel.ForeColor = labelColor;
    }

    private void UpdatePastedTextPrefixState()
    {
        var enabled = _enablePastedTextPrefixCheck.Checked;
        _pastedTextPrefixTextBox.ReadOnly = !enabled;
        _pastedTextPrefixTextBox.TabStop = enabled;

        var theme = GetActiveTheme();
        _pastedTextPrefixTextBox.BackColor = _pastedTextPrefixTextBox.ReadOnly ? theme.ReadOnlyInputBack : theme.InputBack;
        _pastedTextPrefixTextBox.ForeColor = enabled ? theme.Text : theme.MutedText;
    }

    private void SetVoiceCommandsExpanded(bool expanded)
    {
        _voiceCommandsExpanded = expanded;
        _voiceCommandsBody.Visible = expanded;
        _voiceCommandsToggle.Text = $"{(expanded ? "▾" : "▸")} Voice Commands";
    }

    private void UpdateTranscriptionPromptState()
    {
        var enabled = _enableTranscriptionPromptCheck.Checked;
        _transcriptionPromptTextBox.ReadOnly = !enabled;
        _transcriptionPromptTextBox.TabStop = enabled;

        var theme = GetActiveTheme();
        _transcriptionPromptTextBox.BackColor =
            _transcriptionPromptTextBox.ReadOnly ? theme.ReadOnlyInputBack : theme.InputBack;
        _transcriptionPromptTextBox.ForeColor = enabled ? theme.Text : theme.MutedText;
    }

    private void UpdatePenHotkeySettingsState()
    {
        var enabled = _enablePenHotkeyCheck.Checked;
        // Labels do not render theme-aware when disabled; keep them enabled and adjust color instead.
        _penHotkeyLabel.Enabled = true;
        _penHotkeyLabel.ForeColor = enabled ? GetActiveTheme().Text : GetMutedTextColor();
        _penHotkeyBox.Enabled = enabled;
    }

    private SettingsTheme GetActiveTheme() => _settingsDarkModeEnabled ? DarkTheme : LightTheme;

    private Color GetMutedTextColor() => GetActiveTheme().MutedText;

    private void ApplySettingsTheme(bool dark)
    {
        _settingsDarkModeEnabled = dark;
        var theme = GetActiveTheme();

        UpdateComboListBrush(theme);

        SuspendLayout();
        try
        {
            BackColor = theme.WindowBack;
            ForeColor = theme.Text;

            ApplyThemeToControlsRecursive(this, theme);
            UpdatePastedTextPrefixState();
            UpdateTranscriptionPromptState();

            // Keep the current validation state, but update "muted" text to match the theme.
            if (_voiceCommandValidationResult.ForeColor == Color.DimGray ||
                _voiceCommandValidationResult.ForeColor == SystemColors.GrayText ||
                _voiceCommandValidationResult.ForeColor == DarkTheme.MutedText)
            {
                _voiceCommandValidationResult.ForeColor = theme.MutedText;
            }
        }
        finally
        {
            ResumeLayout(performLayout: true);
            Invalidate(true);
        }
    }

    private void UpdateComboListBrush(SettingsTheme theme)
    {
        if (_comboListBackBrush != IntPtr.Zero)
        {
            DeleteObject(_comboListBackBrush);
            _comboListBackBrush = IntPtr.Zero;
        }

        if (theme.IsDark)
            _comboListBackBrush = CreateSolidBrush(ColorTranslator.ToWin32(theme.InputBack));
    }

    private static void ApplyThemeToControlsRecursive(Control root, SettingsTheme theme)
    {
        foreach (Control child in root.Controls)
        {
            ApplyThemeToControl(child, theme);
            ApplyThemeToControlsRecursive(child, theme);
        }
    }

    private void SetupThemedComboBox(ComboBox comboBox)
    {
        comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        comboBox.DrawItem -= OnThemedComboBoxDrawItem;
        comboBox.DrawItem += OnThemedComboBoxDrawItem;
    }

    private void OnThemedComboBoxDrawItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo)
            return;

        // Owner-draw is needed for consistent dark-mode rendering, especially for DropDownList combo boxes
        // where the OS may ignore BackColor.
        var theme = GetActiveTheme();
        var enabled = combo.Enabled;
        var selected = (e.State & DrawItemState.Selected) != 0;

        var bg = enabled ? theme.InputBack : theme.ReadOnlyInputBack;
        var fg = enabled ? theme.Text : theme.MutedText;
        if (selected)
        {
            bg = theme.IsDark ? ControlPaint.Light(theme.InputBack, 0.32f) : SystemColors.Highlight;
            fg = theme.IsDark ? theme.Text : SystemColors.HighlightText;
        }

        using (var bgBrush = new SolidBrush(bg))
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

        var text = e.Index >= 0
            ? combo.GetItemText(combo.Items[e.Index])
            : combo.Text;

        var textBounds = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            text,
            e.Font,
            textBounds,
            fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if ((e.State & DrawItemState.Focus) != 0)
            e.DrawFocusRectangle();
    }

    private static void ApplyThemeToControl(Control control, SettingsTheme theme)
    {
        var parentBack = control.Parent?.BackColor ?? theme.WindowBack;

        switch (control)
        {
            case GroupBox groupBox:
                // Avoid GroupBox caption "cutout" painting artifacts by matching the parent's background.
                groupBox.BackColor = parentBack;
                groupBox.ForeColor = theme.Text;
                if (groupBox is ThemedGroupBox themed)
                    themed.BorderColor = theme.Border;
                return;
            case Panel or TableLayoutPanel or FlowLayoutPanel:
                control.BackColor = parentBack;
                control.ForeColor = theme.Text;
                return;
            case Label label:
                label.BackColor = parentBack;
                if (label.ForeColor == Color.DimGray ||
                    label.ForeColor == SystemColors.GrayText ||
                    label.ForeColor == DarkTheme.MutedText)
                {
                    label.ForeColor = theme.MutedText;
                }
                else if (label.ForeColor == Color.ForestGreen || label.ForeColor == Color.DarkOrange)
                {
                    // Preserve semantic validation colors.
                }
                else
                {
                    label.ForeColor = theme.Text;
                }
                return;
            case CheckBox or RadioButton:
                control.BackColor = parentBack;
                control.ForeColor = theme.Text;
                return;
            case TextBox textBox:
                textBox.BackColor = textBox.ReadOnly ? theme.ReadOnlyInputBack : theme.InputBack;
                textBox.ForeColor = theme.Text;
                return;
            case ComboBox comboBox:
                comboBox.BackColor = theme.InputBack;
                comboBox.ForeColor = theme.Text;
                comboBox.FlatStyle = theme.IsDark ? FlatStyle.Popup : FlatStyle.Standard;
                return;
            case NumericUpDown numericUpDown:
                numericUpDown.BackColor = theme.InputBack;
                numericUpDown.ForeColor = theme.Text;
                return;
            case Button button:
                if (theme.IsDark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = theme.ButtonBack;
                    button.ForeColor = theme.Text;
                    button.FlatAppearance.BorderColor = theme.Border;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(theme.ButtonBack, 0.12f);
                    button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(theme.ButtonBack, 0.12f);
                }
                else
                {
                    button.FlatStyle = FlatStyle.Standard;
                    button.UseVisualStyleBackColor = true;
                    button.BackColor = SystemColors.Control;
                    button.ForeColor = SystemColors.ControlText;
                }
                return;
            default:
                control.BackColor = parentBack;
                control.ForeColor = theme.Text;
                return;
        }
    }

    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        if (!TryMapPenHotkey(e.KeyCode, out var detectedHotkey))
            return;

        UpdatePenHotkeyValidationResult(detectedHotkey, "key event");
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void UpdatePenHotkeyValidationResult(string detectedHotkey, string source)
    {
        var selectedHotkey = AppConfig.NormalizePenHotkey(_penHotkeyBox.SelectedItem?.ToString());
        var matchesSelected = string.Equals(detectedHotkey, selectedHotkey, StringComparison.OrdinalIgnoreCase);
        _penHotkeyValidationResult.ForeColor = matchesSelected ? Color.ForestGreen : Color.DarkOrange;
        _penHotkeyValidationResult.Text = matchesSelected
            ? $"Last detected: {detectedHotkey} ({source}, matches selected key)."
            : $"Last detected: {detectedHotkey} ({source}, selected key is {selectedHotkey}).";
    }

    private static bool TryMapPenHotkey(Keys keyCode, out string hotkey)
    {
        hotkey = keyCode switch
        {
            Keys.F13 => "F13",
            Keys.F14 => "F14",
            Keys.F15 => "F15",
            Keys.F16 => "F16",
            Keys.F17 => "F17",
            Keys.F18 => "F18",
            Keys.F19 => "F19",
            Keys.F20 => "F20",
            Keys.F21 => "F21",
            Keys.F22 => "F22",
            Keys.F23 => "F23",
            Keys.F24 => "F24",
            Keys.LaunchApplication1 => "LaunchApp1",
            Keys.LaunchApplication2 => "LaunchApp2",
            _ => string.Empty
        };

        return hotkey.Length > 0;
    }

    private void UpdateAppInfo()
    {
        _versionValueLabel.Text = AppInfo.Version;
        _startedAtValueLabel.Text = AppInfo.StartedAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
        _uptimeValueLabel.Text = AppInfo.FormatUptime(AppInfo.Uptime);
    }

    private void WrapWindowToContent(Control contentPanel, Control buttonsLayout)
    {
        contentPanel.PerformLayout();
        buttonsLayout.PerformLayout();

        var contentSize = contentPanel.GetPreferredSize(Size.Empty);
        var buttonsSize = buttonsLayout.GetPreferredSize(Size.Empty);
        var desiredWidth = Math.Max(contentSize.Width, buttonsSize.Width) + Padding.Horizontal + 6;
        var desiredHeight = contentSize.Height + buttonsSize.Height + Padding.Vertical + 10;

        var workingArea = Screen.FromControl(this).WorkingArea;
        var clampedWidth = Math.Min(desiredWidth, workingArea.Width - 80);
        var clampedHeight = Math.Min(desiredHeight, workingArea.Height - 80);

        ClientSize = new Size(
            Math.Max(MinimumSize.Width - (Width - ClientSize.Width), clampedWidth),
            Math.Max(MinimumSize.Height - (Height - ClientSize.Height), clampedHeight));
    }

    protected override void WndProc(ref Message m)
    {
        if (_settingsDarkModeEnabled &&
            m.Msg == WM_CTLCOLORLISTBOX &&
            _comboListBackBrush != IntPtr.Zero)
        {
            var listHandle = m.LParam;
            if (listHandle != IntPtr.Zero)
            {
                var theme = GetActiveTheme();
                var hdc = m.WParam;
                SetTextColor(hdc, ColorTranslator.ToWin32(theme.Text));
                SetBkColor(hdc, ColorTranslator.ToWin32(theme.InputBack));
                m.Result = _comboListBackBrush;
                return;
            }
        }

        if (m.Msg == WM_APPCOMMAND)
        {
            var appCommand = (int)(((long)m.LParam >> 16) & 0x7FF);
            var mappedHotkey = appCommand switch
            {
                APPCOMMAND_LAUNCH_APP1 => "LaunchApp1",
                APPCOMMAND_LAUNCH_APP2 => "LaunchApp2",
                _ => null
            };

            if (mappedHotkey != null)
                UpdatePenHotkeyValidationResult(mappedHotkey, "app command");
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uptimeTimer.Stop();
            _uptimeTimer.Dispose();
            _formIcon.Dispose();
            if (_comboListBackBrush != IntPtr.Zero)
            {
                DeleteObject(_comboListBackBrush);
                _comboListBackBrush = IntPtr.Zero;
            }
        }

        base.Dispose(disposing);
    }
}
