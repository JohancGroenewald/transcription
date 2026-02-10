namespace VoiceType;

public class SettingsForm : Form
{
    private const int WM_APPCOMMAND = 0x0319;
    private const int APPCOMMAND_LAUNCH_APP1 = 17;
    private const int APPCOMMAND_LAUNCH_APP2 = 18;

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
    private readonly CheckBox _showOverlayBorderCheck;
    private readonly CheckBox _useSimpleMicSpinnerCheck;
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
    private readonly Label _versionValueLabel;
    private readonly Label _startedAtValueLabel;
    private readonly Label _uptimeValueLabel;
    private readonly System.Windows.Forms.Timer _uptimeTimer;
    private readonly Icon _formIcon;

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
        KeyPreview = true;
        Padding = new Padding(12);
        MinimumSize = new Size(620, 520);
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

        var grpApi = new GroupBox
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

        var grpBehavior = new GroupBox
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
            RowCount = 14,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        behaviorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        behaviorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        behaviorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        behaviorLayout.Controls.Add(_enablePenHotkeyCheck, 0, 10);
        behaviorLayout.SetColumnSpan(_enablePenHotkeyCheck, 2);
        behaviorLayout.Controls.Add(_penHotkeyLabel, 0, 11);
        behaviorLayout.Controls.Add(_penHotkeyBox, 1, 11);
        behaviorLayout.Controls.Add(penValidationLabel, 0, 12);
        behaviorLayout.SetColumnSpan(penValidationLabel, 2);
        behaviorLayout.Controls.Add(_penHotkeyValidationResult, 0, 13);
        behaviorLayout.SetColumnSpan(_penHotkeyValidationResult, 2);
        grpBehavior.Controls.Add(behaviorLayout);

        var grpVoiceCommands = new GroupBox
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
            RowCount = 8,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        voiceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        voiceLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var voiceHelp = new Label
        {
            Text = "Commands are matched exactly (optional 'please' before or after).",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Margin = new Padding(0, 0, 0, 8)
        };

        _openSettingsVoiceCommandCheck = new CheckBox
        {
            Text = "Enable \"open settings\"",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        _exitAppVoiceCommandCheck = new CheckBox
        {
            Text = "Enable \"exit app\"",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        _toggleAutoEnterVoiceCommandCheck = new CheckBox
        {
            Text = "Enable \"auto-send yes/no\"",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        _sendVoiceCommandCheck = new CheckBox
        {
            Text = "Enable \"send\" (press Enter)",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        _showVoiceCommandsVoiceCommandCheck = new CheckBox
        {
            Text = "Enable \"show voice commands\"",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

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
            PlaceholderText = "e.g. auto send no",
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

        voiceLayout.Controls.Add(voiceHelp, 0, 0);
        voiceLayout.Controls.Add(_openSettingsVoiceCommandCheck, 0, 1);
        voiceLayout.Controls.Add(_exitAppVoiceCommandCheck, 0, 2);
        voiceLayout.Controls.Add(_toggleAutoEnterVoiceCommandCheck, 0, 3);
        voiceLayout.Controls.Add(_sendVoiceCommandCheck, 0, 4);
        voiceLayout.Controls.Add(_showVoiceCommandsVoiceCommandCheck, 0, 5);
        voiceLayout.Controls.Add(validatorPanel, 0, 6);
        voiceLayout.Controls.Add(_voiceCommandValidationResult, 0, 7);
        grpVoiceCommands.Controls.Add(voiceLayout);

        var grpAppInfo = new GroupBox
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
        _showOverlayBorderCheck.Checked = config.ShowOverlayBorder;
        _useSimpleMicSpinnerCheck.Checked = config.UseSimpleMicSpinner;
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
        UpdatePenHotkeySettingsState();
        ValidateVoiceCommandInput();
        WrapWindowToContent(contentPanel, buttonsLayout);
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
            ShowOverlayBorder = _showOverlayBorderCheck.Checked,
            UseSimpleMicSpinner = _useSimpleMicSpinnerCheck.Checked,
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
            _voiceCommandValidationResult.ForeColor = Color.DimGray;
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

        _voiceCommandValidationResult.ForeColor = Color.DimGray;
        _voiceCommandValidationResult.Text = "No command match.";
    }

    private void UpdateOverlaySettingsState()
    {
        var enabled = _enableOverlayPopupsCheck.Checked;
        _overlayDurationMsInput.Enabled = enabled;
        _overlayOpacityLabel.Enabled = enabled;
        _overlayOpacityInput.Enabled = enabled;
        _overlayWidthLabel.Enabled = enabled;
        _overlayWidthInput.Enabled = enabled;
        _overlayFontSizeLabel.Enabled = enabled;
        _overlayFontSizeInput.Enabled = enabled;
        _showOverlayBorderCheck.Enabled = enabled;
        _useSimpleMicSpinnerCheck.Enabled = enabled;
    }

    private void UpdatePenHotkeySettingsState()
    {
        var enabled = _enablePenHotkeyCheck.Checked;
        _penHotkeyLabel.Enabled = enabled;
        _penHotkeyBox.Enabled = enabled;
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
        }

        base.Dispose(disposing);
    }
}
