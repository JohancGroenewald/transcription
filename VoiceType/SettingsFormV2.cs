using NAudio.Wave;

namespace VoiceType;

public sealed class SettingsFormV2 : Form
{
    private const int RedesignedMinWidth = 1400;
    private const int RedesignedMinHeight = 780;
    private const int RedesignedPreferredWidth = 1580;
    private const int RedesignedBasePadding = 14;

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

    private readonly Icon _formIcon;
    private readonly TextBox _apiKeyBox;
    private readonly CheckBox _showKeyCheck;
    private readonly ComboBox _modelBox;
    private readonly CheckBox _autoEnterCheck;
    private readonly CheckBox _debugLoggingCheck;
    private readonly CheckBox _overlayPopupsCheck;
    private readonly NumericUpDown _overlayDurationInput;
    private readonly NumericUpDown _overlayOpacityInput;
    private readonly NumericUpDown _overlayWidthInput;
    private readonly NumericUpDown _overlayFontSizeInput;
    private readonly ComboBox _overlayFadeProfileCombo;
    private readonly ComboBox _overlayBackgroundModeCombo;
    private readonly CheckBox _showOverlayBorderCheck;
    private readonly CheckBox _useSimpleMicSpinnerCheck;
    private readonly CheckBox _enablePreviewPlaybackCleanupCheck;
    private readonly ComboBox _microphoneDeviceCombo;
    private readonly ComboBox _audioOutputDeviceCombo;
    private readonly ComboBox _remoteActionPopupCombo;
    private readonly CheckBox _enablePastedTextPrefixCheck;
    private readonly TextBox _pastedTextPrefixTextBox;
    private readonly CheckBox _enableTranscriptionPromptCheck;
    private readonly TextBox _transcriptionPromptTextBox;
    private readonly CheckBox _settingsDarkModeCheck;
    private readonly CheckBox _enablePenHotkeyCheck;
    private readonly ComboBox _penHotkeyBox;
    private readonly CheckBox _openSettingsVoiceCommandCheck;
    private readonly CheckBox _exitAppVoiceCommandCheck;
    private readonly CheckBox _toggleAutoEnterVoiceCommandCheck;
    private readonly CheckBox _sendVoiceCommandCheck;
    private readonly CheckBox _showVoiceCommandsVoiceCommandCheck;
    private readonly CheckBox _remoteListenWhileListeningCheck;
    private readonly CheckBox _remoteListenWhilePreprocessingCheck;
    private readonly CheckBox _remoteListenWhileTextDisplayedCheck;
    private readonly CheckBox _remoteListenWhileCountdownCheck;
    private readonly CheckBox _remoteListenWhileIdleCheck;
    private readonly CheckBox _remoteSubmitWhileListeningCheck;
    private readonly CheckBox _remoteSubmitWhilePreprocessingCheck;
    private readonly CheckBox _remoteSubmitWhileTextDisplayedCheck;
    private readonly CheckBox _remoteSubmitWhileCountdownCheck;
    private readonly CheckBox _remoteSubmitWhileIdleCheck;
    private readonly CheckBox _remoteActivateWhileListeningCheck;
    private readonly CheckBox _remoteActivateWhilePreprocessingCheck;
    private readonly CheckBox _remoteActivateWhileTextDisplayedCheck;
    private readonly CheckBox _remoteActivateWhileCountdownCheck;
    private readonly CheckBox _remoteActivateWhileIdleCheck;
    private readonly CheckBox _remoteCloseWhileListeningCheck;
    private readonly CheckBox _remoteCloseWhilePreprocessingCheck;
    private readonly CheckBox _remoteCloseWhileTextDisplayedCheck;
    private readonly CheckBox _remoteCloseWhileCountdownCheck;
    private readonly CheckBox _remoteCloseWhileIdleCheck;
    private Label _remoteStateFilterSummaryLabel;
    private readonly Label _versionValueLabel;
    private readonly Label _startedAtValueLabel;
    private readonly Label _uptimeValueLabel;
    private readonly System.Windows.Forms.Timer _uptimeTimer;
    private bool _settingsDarkModeEnabled;

    private sealed class ThemedGroupBox : GroupBox
    {
        private readonly Func<Color> _backColorProvider;
        private readonly Func<Color> _borderColorProvider;
        private readonly Func<Color> _textColorProvider;

        public ThemedGroupBox(Func<Color> backColorProvider, Func<Color> borderColorProvider, Func<Color> textColorProvider)
        {
            _backColorProvider = backColorProvider;
            _borderColorProvider = borderColorProvider;
            _textColorProvider = textColorProvider;
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
            g.Clear(_backColorProvider());
            var captionHeight = TextRenderer.MeasureText(g, Text, Font).Height;
            var borderRect = new Rectangle(0, captionHeight / 2, Width - 1, Height - 1 - (captionHeight / 2));
            using (var borderPen = new Pen(_borderColorProvider()))
                g.DrawRectangle(borderPen, borderRect);

            if (!string.IsNullOrEmpty(Text))
            {
                var captionBounds = new Rectangle(10, 0, TextRenderer.MeasureText(g, Text, Font).Width + 4, captionHeight);
                using (var backBrush = new SolidBrush(_backColorProvider()))
                    g.FillRectangle(backBrush, captionBounds);
                TextRenderer.DrawText(
                    g,
                    Text,
                    Font,
                    new Point(10, -1),
                    _textColorProvider(),
                    TextFormatFlags.Default);
            }
        }
    }

    private sealed record AudioDeviceOption(int DeviceIndex, string DeviceName, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    public SettingsFormV2()
    {
        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _formIcon = extractedIcon != null
            ? (Icon)extractedIcon.Clone()
            : (Icon)SystemIcons.Application.Clone();
        extractedIcon?.Dispose();

        Text = "VoiceType Settings (Redesigned)";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = _formIcon;
        Padding = new Padding(RedesignedBasePadding);
        MinimumSize = new Size(RedesignedMinWidth, RedesignedMinHeight);
        Size = new Size(RedesignedPreferredWidth, RedesignedMinHeight);
        KeyPreview = true;

        _apiKeyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            PlaceholderText = "sk-...",
            MinimumSize = new Size(320, 0)
        };
        _showKeyCheck = new CheckBox
        {
            Text = "Show",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _showKeyCheck.CheckedChanged += (_, _) => _apiKeyBox.UseSystemPasswordChar = !_showKeyCheck.Checked;

        _modelBox = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            MinimumSize = new Size(260, 0)
        };
        _modelBox.Items.AddRange(["whisper-1", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"]);

        _autoEnterCheck = new CheckBox { Text = "Press Enter after pasting text", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _debugLoggingCheck = new CheckBox { Text = "Enable file logging (debug only)", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _overlayPopupsCheck = new CheckBox { Text = "Show popup notifications", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        _overlayPopupsCheck.CheckedChanged += (_, _) => UpdateOverlaySettingsState();

        _overlayDurationInput = CreateNumeric(AppConfig.MinOverlayDurationMs, AppConfig.MaxOverlayDurationMs, 250);
        _overlayOpacityInput = CreateNumeric(AppConfig.MinOverlayOpacityPercent, AppConfig.MaxOverlayOpacityPercent, 1);
        _overlayWidthInput = CreateNumeric(AppConfig.MinOverlayWidthPercent, AppConfig.MaxOverlayWidthPercent, 1);
        _overlayFontSizeInput = CreateNumeric(AppConfig.MinOverlayFontSizePt, AppConfig.MaxOverlayFontSizePt, 1);

        _overlayFadeProfileCombo = new ComboBox { Dock = DockStyle.Left, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        _overlayFadeProfileCombo.Items.AddRange([.. AppConfig.OverlayFadeProfiles]);

        _overlayBackgroundModeCombo = new ComboBox { Dock = DockStyle.Left, Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        _overlayBackgroundModeCombo.Items.AddRange([.. AppConfig.OverlayBackgroundModes]);

        _showOverlayBorderCheck = new CheckBox { Text = "Show HUD border line", AutoSize = true };
        _useSimpleMicSpinnerCheck = new CheckBox { Text = "Use simple mic spinner", AutoSize = true };
        _enablePreviewPlaybackCleanupCheck = new CheckBox { Text = "Enable preview playback cleanup", AutoSize = true };

        _microphoneDeviceCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Width = 420 };
        _audioOutputDeviceCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Width = 420 };
        PopulateAudioDeviceCombo(_microphoneDeviceCombo, GetMicrophoneDeviceOptions());
        PopulateAudioDeviceCombo(_audioOutputDeviceCombo, GetOutputDeviceOptions());

        _remoteActionPopupCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        _remoteActionPopupCombo.Items.AddRange(new object[] { "Off", "Basic", "Detailed" });

        _enablePastedTextPrefixCheck = new CheckBox { Text = "Enable pasted text prefix", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        _enablePastedTextPrefixCheck.CheckedChanged += (_, _) => UpdatePastedTextPrefixState();
        _pastedTextPrefixTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            MinimumSize = new Size(0, 80),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Height = 80,
            PlaceholderText = "Optional prefix"
        };

        _enableTranscriptionPromptCheck = new CheckBox { Text = "Enable custom transcription prompt", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
        _enableTranscriptionPromptCheck.CheckedChanged += (_, _) => UpdateTranscriptionPromptState();
        _transcriptionPromptTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            MinimumSize = new Size(0, 90),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            Height = 90,
            PlaceholderText = "Optional transcription prompt"
        };

        _settingsDarkModeCheck = new CheckBox { Text = "Dark mode", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _settingsDarkModeCheck.CheckedChanged += (_, _) => ApplySettingsTheme();

        _enablePenHotkeyCheck = new CheckBox { Text = "Enable Surface Pen hotkey", AutoSize = true };
        _enablePenHotkeyCheck.CheckedChanged += (_, _) => UpdatePenHotkeyState();
        _penHotkeyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        _penHotkeyBox.Items.AddRange(AppConfig.GetSupportedPenHotkeys().Cast<object>().ToArray());

        _openSettingsVoiceCommandCheck = new CheckBox { Text = "Open Settings", AutoSize = true };
        _exitAppVoiceCommandCheck = new CheckBox { Text = "Exit VoiceType", AutoSize = true };
        _toggleAutoEnterVoiceCommandCheck = new CheckBox { Text = "Toggle Enter", AutoSize = true };
        _sendVoiceCommandCheck = new CheckBox { Text = "Send Voice Command", AutoSize = true };
        _showVoiceCommandsVoiceCommandCheck = new CheckBox { Text = "Show Voice Commands", AutoSize = true };
        _remoteListenWhileListeningCheck = new CheckBox { Text = "Listen", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteListenWhilePreprocessingCheck = new CheckBox { Text = "Listen", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteListenWhileTextDisplayedCheck = new CheckBox { Text = "Listen", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteListenWhileCountdownCheck = new CheckBox { Text = "Listen", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteListenWhileIdleCheck = new CheckBox { Text = "Listen", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteSubmitWhileListeningCheck = new CheckBox { Text = "Submit", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteSubmitWhilePreprocessingCheck = new CheckBox { Text = "Submit", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteSubmitWhileTextDisplayedCheck = new CheckBox { Text = "Submit", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteSubmitWhileCountdownCheck = new CheckBox { Text = "Submit", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteSubmitWhileIdleCheck = new CheckBox { Text = "Submit", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteActivateWhileListeningCheck = new CheckBox { Text = "Activate", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteActivateWhilePreprocessingCheck = new CheckBox { Text = "Activate", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteActivateWhileTextDisplayedCheck = new CheckBox { Text = "Activate", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteActivateWhileCountdownCheck = new CheckBox { Text = "Activate", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteActivateWhileIdleCheck = new CheckBox { Text = "Activate", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteCloseWhileListeningCheck = new CheckBox { Text = "Close", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteCloseWhilePreprocessingCheck = new CheckBox { Text = "Close", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteCloseWhileTextDisplayedCheck = new CheckBox { Text = "Close", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteCloseWhileCountdownCheck = new CheckBox { Text = "Close", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        _remoteCloseWhileIdleCheck = new CheckBox { Text = "Close", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        AttachRemoteStateFilterCheckboxes();
        _remoteStateFilterSummaryLabel = new Label
        {
            Text = "Waiting for remote command state settings...",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0),
            ForeColor = Color.DimGray
        };

        _versionValueLabel = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 0), ForeColor = Color.DimGray };
        _startedAtValueLabel = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 0), ForeColor = Color.DimGray };
        _uptimeValueLabel = new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 0), ForeColor = Color.DimGray };

        _uptimeTimer = new System.Windows.Forms.Timer { Interval = 1000, Enabled = true };
        _uptimeTimer.Tick += (_, _) => UpdateAppInfo();

        var apiSection = BuildSection("OpenAI API",
            CreateLabeledRow("API key", _apiKeyBox, _showKeyCheck),
            CreateLabeledRow("Transcription model", _modelBox),
            _autoEnterCheck,
            _debugLoggingCheck);

        var behaviorSection = BuildSection("Behavior",
            _overlayPopupsCheck,
            CreateLabeledRow("Popup duration (ms)", _overlayDurationInput),
            CreateLabeledRow("HUD opacity (%)", _overlayOpacityInput),
            CreateLabeledRow("HUD width (%)", _overlayWidthInput),
            CreateLabeledRow("HUD font size (pt)", _overlayFontSizeInput),
            CreateLabeledRow("HUD fade profile", _overlayFadeProfileCombo),
            CreateLabeledRow("HUD background mode", _overlayBackgroundModeCombo),
            _showOverlayBorderCheck,
            _useSimpleMicSpinnerCheck,
            _enablePreviewPlaybackCleanupCheck);

        var audioSection = BuildSection("Audio & Devices",
            CreateLabeledRow("Microphone input", _microphoneDeviceCombo),
            CreateLabeledRow("Audio output", _audioOutputDeviceCombo),
            CreateLabeledRow("Remote action popup", _remoteActionPopupCombo),
            _enablePastedTextPrefixCheck,
            _pastedTextPrefixTextBox,
            _enableTranscriptionPromptCheck,
            _transcriptionPromptTextBox,
            _settingsDarkModeCheck);

        var commandSection = BuildSection("Commands & hotkeys",
            _openSettingsVoiceCommandCheck,
            _exitAppVoiceCommandCheck,
            _toggleAutoEnterVoiceCommandCheck,
            _sendVoiceCommandCheck,
            _showVoiceCommandsVoiceCommandCheck,
            _enablePenHotkeyCheck,
            CreateLabeledRow("Pen button", _penHotkeyBox));
        var remoteCommandSection = BuildSection(
            "Remote command state filters",
            BuildRemoteCommandStateFilterSection());

        var appInfoSection = BuildSection(
            "App info",
            CreateLabeledRow("Version", _versionValueLabel),
            CreateLabeledRow("Started at", _startedAtValueLabel),
            CreateLabeledRow("Uptime", _uptimeValueLabel));

        var sections = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(0),
            Margin = new Padding(0),
            AutoSize = false
        };
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        sections.Controls.Add(apiSection, 0, 0);
        sections.SetColumnSpan(apiSection, 2);
        sections.Controls.Add(behaviorSection, 0, 1);
        sections.Controls.Add(audioSection, 1, 1);
        sections.Controls.Add(commandSection, 0, 2);
        sections.Controls.Add(appInfoSection, 1, 2);
        sections.Controls.Add(remoteCommandSection, 0, 3);

        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0, 0, 0, 8)
        };
        contentPanel.Controls.Add(sections);

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

        var exitButton = new Button
        {
            Text = "Exit VoiceType",
            MinimumSize = new Size(120, 32),
            AutoSize = true
        };
        exitButton.Click += (_, _) =>
        {
            Close();
            Application.Exit();
        };

        var cancelButton = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.Cancel
        };

        var saveButton = new Button
        {
            Text = "Save",
            AutoSize = true,
            MinimumSize = new Size(90, 32),
            DialogResult = DialogResult.OK
        };
        saveButton.Click += OnSave;

        var rightButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0)
        };
        rightButtons.Controls.Add(saveButton);
        rightButtons.Controls.Add(cancelButton);

        buttonsLayout.Controls.Add(exitButton, 0, 0);
        buttonsLayout.Controls.Add(rightButtons, 2, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(contentPanel, 0, 0);
        root.Controls.Add(buttonsLayout, 0, 1);

        Controls.Add(root);

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        LoadConfigIntoControls();
        UpdateAppInfo();
        ApplySettingsTheme();
        UpdateOverlaySettingsState();
    }

    private static NumericUpDown CreateNumeric(int minimum, int maximum, decimal increment)
    {
        return new NumericUpDown
        {
            Width = 140,
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            DecimalPlaces = 0,
            Dock = DockStyle.Left,
            Margin = new Padding(0, 2, 0, 0)
        };
    }

    private static Control CreateLabeledRow(string labelText, Control control)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0)
        };

        control.Dock = DockStyle.Fill;
        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(control, 1, 0);
        return panel;
    }

    private static Control CreateLabeledRow(string labelText, Control control, Control trailing)
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(0),
            Margin = new Padding(0, 0, 0, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 8, 0)
        };

        control.Dock = DockStyle.Fill;
        trailing.Dock = DockStyle.Left;
        panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(control, 1, 0);
            panel.Controls.Add(trailing, 2, 0);
            return panel;
        }

    private Control BuildRemoteCommandStateFilterSection()
    {
        var remoteStateFiltersLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 5,
            RowCount = 6,
            Margin = new Padding(0, 4, 0, 0),
            Padding = new Padding(0)
        };
        remoteStateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        remoteStateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        remoteStateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        remoteStateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        remoteStateFiltersLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        for (var i = 0; i < remoteStateFiltersLayout.RowCount; i++)
            remoteStateFiltersLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "State",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 10, 2)
        }, 0, 0);
        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Listen",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 10, 2)
        }, 1, 0);
        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Submit",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 10, 2)
        }, 2, 0);
        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Activate",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 10, 2)
        }, 3, 0);
        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Close",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2)
        }, 4, 0);

        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Listening",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 10, 2)
        }, 0, 1);
        remoteStateFiltersLayout.Controls.Add(_remoteListenWhileListeningCheck, 1, 1);
        remoteStateFiltersLayout.Controls.Add(_remoteSubmitWhileListeningCheck, 2, 1);
        remoteStateFiltersLayout.Controls.Add(_remoteActivateWhileListeningCheck, 3, 1);
        remoteStateFiltersLayout.Controls.Add(_remoteCloseWhileListeningCheck, 4, 1);

        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Pre-processing",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 10, 2)
        }, 0, 2);
        remoteStateFiltersLayout.Controls.Add(_remoteListenWhilePreprocessingCheck, 1, 2);
        remoteStateFiltersLayout.Controls.Add(_remoteSubmitWhilePreprocessingCheck, 2, 2);
        remoteStateFiltersLayout.Controls.Add(_remoteActivateWhilePreprocessingCheck, 3, 2);
        remoteStateFiltersLayout.Controls.Add(_remoteCloseWhilePreprocessingCheck, 4, 2);

        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Text displayed",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 10, 2)
        }, 0, 3);
        remoteStateFiltersLayout.Controls.Add(_remoteListenWhileTextDisplayedCheck, 1, 3);
        remoteStateFiltersLayout.Controls.Add(_remoteSubmitWhileTextDisplayedCheck, 2, 3);
        remoteStateFiltersLayout.Controls.Add(_remoteActivateWhileTextDisplayedCheck, 3, 3);
        remoteStateFiltersLayout.Controls.Add(_remoteCloseWhileTextDisplayedCheck, 4, 3);

        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Auto-submit countdown",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 10, 2)
        }, 0, 4);
        remoteStateFiltersLayout.Controls.Add(_remoteListenWhileCountdownCheck, 1, 4);
        remoteStateFiltersLayout.Controls.Add(_remoteSubmitWhileCountdownCheck, 2, 4);
        remoteStateFiltersLayout.Controls.Add(_remoteActivateWhileCountdownCheck, 3, 4);
        remoteStateFiltersLayout.Controls.Add(_remoteCloseWhileCountdownCheck, 4, 4);

        remoteStateFiltersLayout.Controls.Add(new Label
        {
            Text = "Idle",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 2, 10, 2)
        }, 0, 5);
        remoteStateFiltersLayout.Controls.Add(_remoteListenWhileIdleCheck, 1, 5);
        remoteStateFiltersLayout.Controls.Add(_remoteSubmitWhileIdleCheck, 2, 5);
        remoteStateFiltersLayout.Controls.Add(_remoteActivateWhileIdleCheck, 3, 5);
        remoteStateFiltersLayout.Controls.Add(_remoteCloseWhileIdleCheck, 4, 5);

        _remoteStateFilterSummaryLabel.Text = "Waiting for remote command state settings...";
        UpdateRemoteStateFilterSummary();

        var remoteStateFilterContainer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        remoteStateFilterContainer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        remoteStateFilterContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        remoteStateFilterContainer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        remoteStateFilterContainer.Controls.Add(remoteStateFiltersLayout, 0, 0);
        remoteStateFilterContainer.Controls.Add(_remoteStateFilterSummaryLabel, 0, 1);

        return remoteStateFilterContainer;
    }

    private void AttachRemoteStateFilterCheckboxes()
    {
        var stateFilterCheckBoxes = new[]
        {
            _remoteListenWhileListeningCheck,
            _remoteListenWhilePreprocessingCheck,
            _remoteListenWhileTextDisplayedCheck,
            _remoteListenWhileCountdownCheck,
            _remoteListenWhileIdleCheck,
            _remoteSubmitWhileListeningCheck,
            _remoteSubmitWhilePreprocessingCheck,
            _remoteSubmitWhileTextDisplayedCheck,
            _remoteSubmitWhileCountdownCheck,
            _remoteSubmitWhileIdleCheck,
            _remoteActivateWhileListeningCheck,
            _remoteActivateWhilePreprocessingCheck,
            _remoteActivateWhileTextDisplayedCheck,
            _remoteActivateWhileCountdownCheck,
            _remoteActivateWhileIdleCheck,
            _remoteCloseWhileListeningCheck,
            _remoteCloseWhilePreprocessingCheck,
            _remoteCloseWhileTextDisplayedCheck,
            _remoteCloseWhileCountdownCheck,
            _remoteCloseWhileIdleCheck
        };

        foreach (var checkBox in stateFilterCheckBoxes)
            checkBox.CheckedChanged += (_, _) => UpdateRemoteStateFilterSummary();
    }

    private void UpdateRemoteStateFilterSummary()
    {
        var listenSummary = BuildRemoteActionStateSummary(
            "Listen",
            _remoteListenWhileListeningCheck.Checked,
            _remoteListenWhilePreprocessingCheck.Checked,
            _remoteListenWhileTextDisplayedCheck.Checked,
            _remoteListenWhileCountdownCheck.Checked,
            _remoteListenWhileIdleCheck.Checked);
        var submitSummary = BuildRemoteActionStateSummary(
            "Submit",
            _remoteSubmitWhileListeningCheck.Checked,
            _remoteSubmitWhilePreprocessingCheck.Checked,
            _remoteSubmitWhileTextDisplayedCheck.Checked,
            _remoteSubmitWhileCountdownCheck.Checked,
            _remoteSubmitWhileIdleCheck.Checked);
        var activateSummary = BuildRemoteActionStateSummary(
            "Activate",
            _remoteActivateWhileListeningCheck.Checked,
            _remoteActivateWhilePreprocessingCheck.Checked,
            _remoteActivateWhileTextDisplayedCheck.Checked,
            _remoteActivateWhileCountdownCheck.Checked,
            _remoteActivateWhileIdleCheck.Checked);
        var closeSummary = BuildRemoteActionStateSummary(
            "Close",
            _remoteCloseWhileListeningCheck.Checked,
            _remoteCloseWhilePreprocessingCheck.Checked,
            _remoteCloseWhileTextDisplayedCheck.Checked,
            _remoteCloseWhileCountdownCheck.Checked,
            _remoteCloseWhileIdleCheck.Checked);

        _remoteStateFilterSummaryLabel.Text = string.Join(" | ", listenSummary, submitSummary, activateSummary, closeSummary);
    }

    private static string BuildRemoteActionStateSummary(
        string action,
        bool listeningState,
        bool preprocessingState,
        bool textDisplayedState,
        bool countdownState,
        bool idleState)
    {
        var statesText = "";
        if (listeningState)
            statesText = AppendRemoteState(statesText, "Listening");
        if (preprocessingState)
            statesText = AppendRemoteState(statesText, "Pre-processing");
        if (textDisplayedState)
            statesText = AppendRemoteState(statesText, "Text displayed");
        if (countdownState)
            statesText = AppendRemoteState(statesText, "Auto-submit countdown");
        if (idleState)
            statesText = AppendRemoteState(statesText, "Idle");

        if (string.IsNullOrWhiteSpace(statesText))
            statesText = "None";

        return $"{action}: {statesText}";
    }

    private static string AppendRemoteState(string states, string state)
        => string.IsNullOrWhiteSpace(states) ? state : $"{states}, {state}";

    private GroupBox BuildSection(string title, params Control[] rows)
    {
        var section = new ThemedGroupBox(GetWindowBack, GetBorder, GetThemeText)
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = Math.Max(1, rows.Length),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (var i = 0; i < layout.RowCount; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        foreach (var row in rows)
            layout.Controls.Add(row);

        section.Controls.Add(layout);
        return section;
    }

    private Color GetWindowBack() => GetTheme().PanelBack;
    private Color GetBorder() => GetTheme().Border;
    private Color GetThemeText() => GetTheme().Text;

    private void LoadConfigIntoControls()
    {
        var config = AppConfig.Load();
        _apiKeyBox.Text = config.ApiKey;
        _modelBox.SelectedItem = config.Model;
        if (_modelBox.SelectedIndex < 0)
            _modelBox.SelectedIndex = 0;

        _autoEnterCheck.Checked = config.AutoEnter;
        _debugLoggingCheck.Checked = config.EnableDebugLogging;
        _overlayPopupsCheck.Checked = config.EnableOverlayPopups;
        _overlayDurationInput.Value = AppConfig.NormalizeOverlayDuration(config.OverlayDurationMs);
        _overlayOpacityInput.Value = AppConfig.NormalizeOverlayOpacityPercent(config.OverlayOpacityPercent);
        _overlayWidthInput.Value = AppConfig.NormalizeOverlayWidthPercent(config.OverlayWidthPercent);
        _overlayFontSizeInput.Value = AppConfig.NormalizeOverlayFontSizePt(config.OverlayFontSizePt);
        _overlayFadeProfileCombo.SelectedIndex = Math.Clamp(
            AppConfig.NormalizeOverlayFadeProfile(config.OverlayFadeProfile),
            0,
            _overlayFadeProfileCombo.Items.Count - 1);
        _overlayBackgroundModeCombo.SelectedIndex = Math.Clamp(
            AppConfig.NormalizeOverlayBackgroundMode(config.OverlayBackgroundMode),
            0,
            _overlayBackgroundModeCombo.Items.Count - 1);
        _showOverlayBorderCheck.Checked = config.ShowOverlayBorder;
        _useSimpleMicSpinnerCheck.Checked = config.UseSimpleMicSpinner;
        _enablePreviewPlaybackCleanupCheck.Checked = config.EnablePreviewPlaybackCleanup;
        SetSelectedAudioDevice(_microphoneDeviceCombo, config.MicrophoneInputDeviceIndex, config.MicrophoneInputDeviceName);
        SetSelectedAudioDevice(_audioOutputDeviceCombo, config.AudioOutputDeviceIndex, config.AudioOutputDeviceName);
        _remoteActionPopupCombo.SelectedIndex = Math.Clamp(
            AppConfig.NormalizeRemoteActionPopupLevel(config.RemoteActionPopupLevel),
            0,
            _remoteActionPopupCombo.Items.Count - 1);
        _enablePastedTextPrefixCheck.Checked = config.EnablePastedTextPrefix;
        _pastedTextPrefixTextBox.Text = config.PastedTextPrefix ?? string.Empty;
        _enableTranscriptionPromptCheck.Checked = config.EnableTranscriptionPrompt;
        _transcriptionPromptTextBox.Text = config.TranscriptionPrompt ?? string.Empty;
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
        _remoteListenWhileListeningCheck.Checked = config.EnableRemoteListenWhileListening;
        _remoteListenWhilePreprocessingCheck.Checked = config.EnableRemoteListenWhilePreprocessing;
        _remoteListenWhileTextDisplayedCheck.Checked = config.EnableRemoteListenWhileTextDisplayed;
        _remoteListenWhileCountdownCheck.Checked = config.EnableRemoteListenWhileCountdown;
        _remoteListenWhileIdleCheck.Checked = config.EnableRemoteListenWhileIdle;
        _remoteSubmitWhileListeningCheck.Checked = config.EnableRemoteSubmitWhileListening;
        _remoteSubmitWhilePreprocessingCheck.Checked = config.EnableRemoteSubmitWhilePreprocessing;
        _remoteSubmitWhileTextDisplayedCheck.Checked = config.EnableRemoteSubmitWhileTextDisplayed;
        _remoteSubmitWhileCountdownCheck.Checked = config.EnableRemoteSubmitWhileCountdown;
        _remoteSubmitWhileIdleCheck.Checked = config.EnableRemoteSubmitWhileIdle;
        _remoteActivateWhileListeningCheck.Checked = config.EnableRemoteActivateWhileListening;
        _remoteActivateWhilePreprocessingCheck.Checked = config.EnableRemoteActivateWhilePreprocessing;
        _remoteActivateWhileTextDisplayedCheck.Checked = config.EnableRemoteActivateWhileTextDisplayed;
        _remoteActivateWhileCountdownCheck.Checked = config.EnableRemoteActivateWhileCountdown;
        _remoteActivateWhileIdleCheck.Checked = config.EnableRemoteActivateWhileIdle;
        _remoteCloseWhileListeningCheck.Checked = config.EnableRemoteCloseWhileListening;
        _remoteCloseWhilePreprocessingCheck.Checked = config.EnableRemoteCloseWhilePreprocessing;
        _remoteCloseWhileTextDisplayedCheck.Checked = config.EnableRemoteCloseWhileTextDisplayed;
        _remoteCloseWhileCountdownCheck.Checked = config.EnableRemoteCloseWhileCountdown;
        _remoteCloseWhileIdleCheck.Checked = config.EnableRemoteCloseWhileIdle;

        UpdatePastedTextPrefixState();
        UpdateTranscriptionPromptState();
        UpdateOverlaySettingsState();
        UpdatePenHotkeyState();
        UpdateRemoteStateFilterSummary();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.None;

        var config = AppConfig.Load();
        config.ApiKey = _apiKeyBox.Text.Trim();
        config.Model = _modelBox.SelectedItem?.ToString() ?? "whisper-1";
        config.AutoEnter = _autoEnterCheck.Checked;
        config.EnableDebugLogging = _debugLoggingCheck.Checked;
        config.EnableOverlayPopups = _overlayPopupsCheck.Checked;
        config.OverlayDurationMs = AppConfig.NormalizeOverlayDuration((int)_overlayDurationInput.Value);
        config.OverlayOpacityPercent = AppConfig.NormalizeOverlayOpacityPercent((int)_overlayOpacityInput.Value);
        config.OverlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent((int)_overlayWidthInput.Value);
        config.OverlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt((int)_overlayFontSizeInput.Value);
        config.OverlayFadeProfile = Math.Clamp(_overlayFadeProfileCombo.SelectedIndex, AppConfig.MinOverlayFadeProfile, AppConfig.MaxOverlayFadeProfile);
        config.OverlayBackgroundMode = Math.Clamp(_overlayBackgroundModeCombo.SelectedIndex, AppConfig.MinOverlayBackgroundMode, AppConfig.MaxOverlayBackgroundMode);
        config.ShowOverlayBorder = _showOverlayBorderCheck.Checked;
        config.UseSimpleMicSpinner = _useSimpleMicSpinnerCheck.Checked;
        config.EnablePreviewPlaybackCleanup = _enablePreviewPlaybackCleanupCheck.Checked;
        config.MicrophoneInputDeviceIndex = GetSelectedAudioDeviceIndex(_microphoneDeviceCombo);
        config.MicrophoneInputDeviceName = GetSelectedAudioDeviceName(_microphoneDeviceCombo);
        config.AudioOutputDeviceIndex = GetSelectedAudioDeviceIndex(_audioOutputDeviceCombo);
        config.AudioOutputDeviceName = GetSelectedAudioDeviceName(_audioOutputDeviceCombo);
        config.RemoteActionPopupLevel = Math.Clamp(_remoteActionPopupCombo.SelectedIndex, AppConfig.MinRemoteActionPopupLevel, AppConfig.MaxRemoteActionPopupLevel);
        config.EnablePastedTextPrefix = _enablePastedTextPrefixCheck.Checked;
        config.PastedTextPrefix = _pastedTextPrefixTextBox.Text;
        config.EnableTranscriptionPrompt = _enableTranscriptionPromptCheck.Checked;
        config.TranscriptionPrompt = _transcriptionPromptTextBox.Text;
        config.EnableSettingsDarkMode = _settingsDarkModeCheck.Checked;
        config.EnablePenHotkey = _enablePenHotkeyCheck.Checked;
        config.PenHotkey = AppConfig.NormalizePenHotkey(_penHotkeyBox.SelectedItem?.ToString());
        config.EnableOpenSettingsVoiceCommand = _openSettingsVoiceCommandCheck.Checked;
        config.EnableExitAppVoiceCommand = _exitAppVoiceCommandCheck.Checked;
        config.EnableToggleAutoEnterVoiceCommand = _toggleAutoEnterVoiceCommandCheck.Checked;
        config.EnableSendVoiceCommand = _sendVoiceCommandCheck.Checked;
        config.EnableShowVoiceCommandsVoiceCommand = _showVoiceCommandsVoiceCommandCheck.Checked;
        config.EnableRemoteListenWhileListening = _remoteListenWhileListeningCheck.Checked;
        config.EnableRemoteListenWhilePreprocessing = _remoteListenWhilePreprocessingCheck.Checked;
        config.EnableRemoteListenWhileTextDisplayed = _remoteListenWhileTextDisplayedCheck.Checked;
        config.EnableRemoteListenWhileCountdown = _remoteListenWhileCountdownCheck.Checked;
        config.EnableRemoteListenWhileIdle = _remoteListenWhileIdleCheck.Checked;
        config.EnableRemoteSubmitWhileListening = _remoteSubmitWhileListeningCheck.Checked;
        config.EnableRemoteSubmitWhilePreprocessing = _remoteSubmitWhilePreprocessingCheck.Checked;
        config.EnableRemoteSubmitWhileTextDisplayed = _remoteSubmitWhileTextDisplayedCheck.Checked;
        config.EnableRemoteSubmitWhileCountdown = _remoteSubmitWhileCountdownCheck.Checked;
        config.EnableRemoteSubmitWhileIdle = _remoteSubmitWhileIdleCheck.Checked;
        config.EnableRemoteActivateWhileListening = _remoteActivateWhileListeningCheck.Checked;
        config.EnableRemoteActivateWhilePreprocessing = _remoteActivateWhilePreprocessingCheck.Checked;
        config.EnableRemoteActivateWhileTextDisplayed = _remoteActivateWhileTextDisplayedCheck.Checked;
        config.EnableRemoteActivateWhileCountdown = _remoteActivateWhileCountdownCheck.Checked;
        config.EnableRemoteActivateWhileIdle = _remoteActivateWhileIdleCheck.Checked;
        config.EnableRemoteCloseWhileListening = _remoteCloseWhileListeningCheck.Checked;
        config.EnableRemoteCloseWhilePreprocessing = _remoteCloseWhilePreprocessingCheck.Checked;
        config.EnableRemoteCloseWhileTextDisplayed = _remoteCloseWhileTextDisplayedCheck.Checked;
        config.EnableRemoteCloseWhileCountdown = _remoteCloseWhileCountdownCheck.Checked;
        config.EnableRemoteCloseWhileIdle = _remoteCloseWhileIdleCheck.Checked;

        try
        {
            config.Save();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings in redesigned form", ex);
            MessageBox.Show(
                "Failed to save settings. Please check file permissions and try again.",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

    private void UpdateOverlaySettingsState()
    {
        var enabled = _overlayPopupsCheck.Checked;
        _overlayDurationInput.Enabled = enabled;
        _overlayOpacityInput.Enabled = enabled;
        _overlayWidthInput.Enabled = enabled;
        _overlayFontSizeInput.Enabled = enabled;
        _overlayFadeProfileCombo.Enabled = enabled;
        _overlayBackgroundModeCombo.Enabled = enabled;
        _showOverlayBorderCheck.Enabled = enabled;
        _useSimpleMicSpinnerCheck.Enabled = enabled;
        _enablePreviewPlaybackCleanupCheck.Enabled = enabled;
    }

    private void UpdatePastedTextPrefixState()
    {
        _pastedTextPrefixTextBox.ReadOnly = !_enablePastedTextPrefixCheck.Checked;
        var theme = GetTheme();
        _pastedTextPrefixTextBox.BackColor = _pastedTextPrefixTextBox.ReadOnly ? theme.ReadOnlyInputBack : theme.InputBack;
        _pastedTextPrefixTextBox.ForeColor = _enablePastedTextPrefixCheck.Checked ? theme.Text : theme.MutedText;
    }

    private void UpdateTranscriptionPromptState()
    {
        _transcriptionPromptTextBox.ReadOnly = !_enableTranscriptionPromptCheck.Checked;
        var theme = GetTheme();
        _transcriptionPromptTextBox.BackColor = _transcriptionPromptTextBox.ReadOnly ? theme.ReadOnlyInputBack : theme.InputBack;
        _transcriptionPromptTextBox.ForeColor = _enableTranscriptionPromptCheck.Checked ? theme.Text : theme.MutedText;
    }

    private void UpdatePenHotkeyState()
    {
        _penHotkeyBox.Enabled = _enablePenHotkeyCheck.Checked;
    }

    private void ApplySettingsTheme()
    {
        _settingsDarkModeEnabled = _settingsDarkModeCheck.Checked;
        ApplyThemeToControls(this, GetTheme());
        ApplyThemeToControls(_apiKeyBox, GetTheme());
        UpdatePastedTextPrefixState();
        UpdateTranscriptionPromptState();
    }

    private void ApplyThemeToControls(Control control, SettingsTheme theme)
    {
        control.BackColor = control switch
        {
            Button => theme.ButtonBack,
            TextBox textBox => textBox.ReadOnly ? theme.ReadOnlyInputBack : theme.InputBack,
            ComboBox => theme.InputBack,
            NumericUpDown => theme.InputBack,
            CheckBox or Label or Panel or TableLayoutPanel or FlowLayoutPanel or GroupBox => theme.PanelBack,
            _ => theme.WindowBack
        };

        control.ForeColor = control is Label && control.ForeColor == Color.DimGray
            ? theme.MutedText
            : theme.Text;

        if (control is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(theme.ButtonBack, 0.1f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(theme.ButtonBack, 0.1f);
        }

        foreach (Control child in control.Controls)
            ApplyThemeToControls(child, theme);
    }

    private SettingsTheme GetTheme() => _settingsDarkModeEnabled ? DarkTheme : LightTheme;

    private void UpdateAppInfo()
    {
        _versionValueLabel.Text = AppInfo.Version;
        _startedAtValueLabel.Text = AppInfo.StartedAtLocal.ToString("yyyy-MM-dd HH:mm:ss");
        _uptimeValueLabel.Text = AppInfo.FormatUptime(AppInfo.Uptime);
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
                options.Add(new AudioDeviceOption(i, deviceName, $"[{i}] {deviceName}"));
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

    private static void SetSelectedAudioDevice(ComboBox combo, int selectedDeviceIndex, string? selectedDeviceName)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _uptimeTimer.Stop();
            _uptimeTimer.Dispose();
            _formIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
