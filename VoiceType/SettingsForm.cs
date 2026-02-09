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
        Padding = new Padding(16);
        ClientSize = new Size(400, 390);

        // --- API Key section ---
        var grpApi = new GroupBox
        {
            Text = "OpenAI API",
            Dock = DockStyle.Top,
            Height = 110,
            Padding = new Padding(12, 8, 12, 8)
        };

        var lblKey = new Label
        {
            Text = "API Key:",
            Location = new Point(14, 24),
            AutoSize = true
        };

        _apiKeyBox = new TextBox
        {
            Location = new Point(14, 44),
            Width = 340,
            UseSystemPasswordChar = true
        };

        _showKeyCheck = new CheckBox
        {
            Text = "Show",
            Location = new Point(14, 72),
            AutoSize = true
        };
        _showKeyCheck.CheckedChanged += (s, e) =>
            _apiKeyBox.UseSystemPasswordChar = !_showKeyCheck.Checked;

        grpApi.Controls.AddRange([lblKey, _apiKeyBox, _showKeyCheck]);

        // --- Options section ---
        var grpOptions = new GroupBox
        {
            Text = "Options",
            Location = new Point(16, 126),
            Size = new Size(368, 184),
            Padding = new Padding(12, 8, 12, 8)
        };

        var lblModel = new Label
        {
            Text = "Transcription model:",
            Location = new Point(14, 24),
            AutoSize = true
        };

        _modelBox = new ComboBox
        {
            Location = new Point(160, 21),
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _modelBox.Items.AddRange(["whisper-1", "gpt-4o-transcribe", "gpt-4o-mini-transcribe"]);

        _autoEnterCheck = new CheckBox
        {
            Text = "Press Enter after pasting text",
            Location = new Point(14, 56),
            AutoSize = true
        };

        _debugLoggingCheck = new CheckBox
        {
            Text = "Enable file logging (debug only)",
            Location = new Point(14, 80),
            AutoSize = true
        };

        _openSettingsVoiceCommandCheck = new CheckBox
        {
            Text = "Voice command: \"open settings\"",
            Location = new Point(14, 104),
            AutoSize = true
        };

        _exitAppVoiceCommandCheck = new CheckBox
        {
            Text = "Voice command: \"exit app\"",
            Location = new Point(14, 128),
            AutoSize = true
        };

        _toggleAutoEnterVoiceCommandCheck = new CheckBox
        {
            Text = "Voice command: \"toggle auto enter\"",
            Location = new Point(14, 152),
            AutoSize = true
        };

        grpOptions.Controls.AddRange([
            lblModel,
            _modelBox,
            _autoEnterCheck,
            _debugLoggingCheck,
            _openSettingsVoiceCommandCheck,
            _exitAppVoiceCommandCheck,
            _toggleAutoEnterVoiceCommandCheck
        ]);

        // --- Buttons ---
        var btnExit = new Button
        {
            Text = "Exit VoiceType",
            Width = 110,
            Height = 30,
            Location = new Point(16, ClientSize.Height - 42),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        btnExit.Click += (s, e) =>
        {
            Close();
            Application.Exit();
        };

        var btnSave = new Button
        {
            Text = "Save",
            Width = 80,
            Height = 30,
            DialogResult = DialogResult.OK
        };
        btnSave.Location = new Point(ClientSize.Width - btnSave.Width - 16, ClientSize.Height - 42);
        btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnSave.Click += OnSave;

        var btnCancel = new Button
        {
            Text = "Cancel",
            Width = 80,
            Height = 30,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.Location = new Point(btnSave.Left - btnCancel.Width - 8, btnSave.Top);
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        Controls.AddRange([grpApi, grpOptions, btnSave, btnCancel, btnExit]);

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
