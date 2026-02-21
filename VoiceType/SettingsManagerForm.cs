namespace VoiceType;

public sealed class SettingsManagerForm : Form
{
    public bool SettingsImported { get; private set; }

    public SettingsManagerForm()
    {
        var extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var formIcon = extractedIcon != null
            ? (Icon)extractedIcon.Clone()
            : (Icon)SystemIcons.Application.Clone();
        extractedIcon?.Dispose();

        Text = "VoiceType Settings";
        Icon = formIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(420, 220);
        Size = new Size(520, 220);

        var description = new Label
        {
            Text = "Use JSON import/export to manage VoiceType settings.",
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 12)
        };

        var importButton = new Button
        {
            Text = "Import settings",
            AutoSize = true,
            MinimumSize = new Size(130, 32)
        };
        importButton.Click += OnImportSettings;

        var exportButton = new Button
        {
            Text = "Export settings",
            AutoSize = true,
            MinimumSize = new Size(130, 32)
        };
        exportButton.Click += OnExportSettings;

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true,
            MinimumSize = new Size(100, 32),
            DialogResult = DialogResult.Cancel
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 10)
        };
        buttons.Controls.Add(importButton);
        buttons.Controls.Add(exportButton);
        buttons.Controls.Add(closeButton);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(description, 0, 0);
        root.Controls.Add(buttons, 0, 1);

        Controls.Add(root);
        CancelButton = closeButton;

        Disposed += (_, _) => formIcon.Dispose();
    }

    private void OnExportSettings(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "VoiceType settings (*.json)|*.json|All files (*.*)|*.*",
            AddExtension = true,
            DefaultExt = "json",
            FileName = $"voicetype-settings-{DateTime.UtcNow:yyyyMMddHHmmss}.json"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            AppConfig.Load().Save(dialog.FileName);
            MessageBox.Show(
                $"Settings exported to:\n{dialog.FileName}",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to export settings.", ex);
            MessageBox.Show(
                "Failed to export settings. Make sure the destination path is writable.",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnImportSettings(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "VoiceType settings (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var config = AppConfig.LoadFromConfigPath(dialog.FileName);
            config.Save();
            SettingsImported = true;
            MessageBox.Show(
                $"Settings imported from:\n{dialog.FileName}",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to import settings.", ex);
            MessageBox.Show(
                "Failed to import settings. Ensure the selected file is a valid VoiceType settings JSON file.",
                "VoiceType",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
