namespace VoiceType;

public class SettingsForm : Form
{
    private readonly TextBox _apiKeyBox;
    private readonly ComboBox _modelBox;
    private readonly CheckBox _showKeyCheck;
    private readonly CheckBox _autoEnterCheck;
    private readonly CheckBox _debugLoggingCheck;
    private readonly CheckBox _openSettingsVoiceCommandCheck;
    private readonly CheckBox _exitAppVoiceCommandCheck;
    private readonly CheckBox _toggleAutoEnterVoiceCommandCheck;
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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(12);
        ClientSize = new Size(500, 455);

        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        // --- API Key section ---
        var grpApi = new GroupBox
        {
            Text = "OpenAI API",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var apiLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2
        };
        apiLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        apiLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        apiLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        apiLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblKey = new Label
        {
            Text = "API Key",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        _apiKeyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            UseSystemPasswordChar = true,
            Margin = new Padding(0, 0, 8, 0)
        };

        _showKeyCheck = new CheckBox
        {
            Text = "Show",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        _showKeyCheck.CheckedChanged += (s, e) =>
            _apiKeyBox.UseSystemPasswordChar = !_showKeyCheck.Checked;

        apiLayout.Controls.Add(lblKey, 0, 0);
        apiLayout.SetColumnSpan(lblKey, 2);
        apiLayout.Controls.Add(_apiKeyBox, 0, 1);
        apiLayout.Controls.Add(_showKeyCheck, 1, 1);
        grpApi.Controls.Add(apiLayout);

        // --- Options section ---
        var grpOptions = new GroupBox
        {
            Text = "Options",
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 10)
        };

        var optionsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 3
        };
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        optionsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        optionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

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
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _modelBox.Items.AddRange(["whisper-1", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"]);

        _autoEnterCheck = new CheckBox
        {
            Text = "Press Enter after pasting text",
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        _debugLoggingCheck = new CheckBox
        {
            Text = "Enable file logging (debug only)",
            AutoSize = true,
            Margin = new Padding(0, 6, 0, 0)
        };

        optionsLayout.Controls.Add(lblModel, 0, 0);
        optionsLayout.Controls.Add(_modelBox, 1, 0);
        optionsLayout.Controls.Add(_autoEnterCheck, 0, 1);
        optionsLayout.SetColumnSpan(_autoEnterCheck, 2);
        optionsLayout.Controls.Add(_debugLoggingCheck, 0, 2);
        optionsLayout.SetColumnSpan(_debugLoggingCheck, 2);
        grpOptions.Controls.Add(optionsLayout);

        // --- Voice Commands section ---
        var grpVoiceCommands = new GroupBox
        {
            Text = "Voice Commands",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 12),
            Margin = new Padding(0, 0, 0, 8)
        };

        var voiceCommandsLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        _openSettingsVoiceCommandCheck = new CheckBox { Text = "Enable \"open settings\"", AutoSize = true };
        _exitAppVoiceCommandCheck = new CheckBox { Text = "Enable \"exit app\"", AutoSize = true };
        _toggleAutoEnterVoiceCommandCheck = new CheckBox
        {
            Text = "Enable \"enable/disable auto-enter\"",
            AutoSize = true
        };
        voiceCommandsLayout.Controls.Add(_openSettingsVoiceCommandCheck);
        voiceCommandsLayout.Controls.Add(_exitAppVoiceCommandCheck);
        voiceCommandsLayout.Controls.Add(_toggleAutoEnterVoiceCommandCheck);
        grpVoiceCommands.Controls.Add(voiceCommandsLayout);

        // --- Buttons ---
        var btnExit = new Button
        {
            Text = "Exit VoiceType",
            AutoSize = true,
            MinimumSize = new Size(110, 32),
            Anchor = AnchorStyles.Left
        };
        btnExit.Click += (s, e) =>
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
            Dock = DockStyle.None,
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
            AutoSize = false,
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

        rootLayout.Controls.Add(grpApi, 0, 0);
        rootLayout.Controls.Add(grpOptions, 0, 1);
        rootLayout.Controls.Add(grpVoiceCommands, 0, 2);
        rootLayout.Controls.Add(buttonsLayout, 0, 3);
        Controls.Add(rootLayout);

        // Load existing config
        var config = AppConfig.Load();
        _apiKeyBox.Text = config.ApiKey;
        _modelBox.SelectedItem = config.Model;
        if (_modelBox.SelectedIndex < 0) _modelBox.SelectedIndex = 0;
        _autoEnterCheck.Checked = config.AutoEnter;
        _debugLoggingCheck.Checked = config.EnableDebugLogging;
        _openSettingsVoiceCommandCheck.Checked = config.EnableOpenSettingsVoiceCommand;
        _exitAppVoiceCommandCheck.Checked = config.EnableExitAppVoiceCommand;
        _toggleAutoEnterVoiceCommandCheck.Checked = config.EnableToggleAutoEnterVoiceCommand;
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
            EnableOpenSettingsVoiceCommand = _openSettingsVoiceCommandCheck.Checked,
            EnableExitAppVoiceCommand = _exitAppVoiceCommandCheck.Checked,
            EnableToggleAutoEnterVoiceCommand = _toggleAutoEnterVoiceCommandCheck.Checked
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

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _formIcon.Dispose();

        base.Dispose(disposing);
    }
}
