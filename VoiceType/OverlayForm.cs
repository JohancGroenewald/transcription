namespace VoiceType;

public class OverlayForm : Form
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Opacity = 0.9;
        Size = new Size(350, 50);
        Padding = new Padding(12, 8, 12, 8);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11f, FontStyle.Regular),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        // Position: bottom-right, above taskbar
        PositionOnScreen();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOPMOST | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    // Prevent the form from stealing focus when shown
    protected override bool ShowWithoutActivation => true;

    private void PositionOnScreen()
    {
        var screen = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(
            screen.Right - Width - 16,
            screen.Bottom - Height - 16);
    }

    public void ShowMessage(string text, Color? color = null, int durationMs = 3000)
    {
        if (InvokeRequired)
        {
            Invoke(() => ShowMessage(text, color, durationMs));
            return;
        }

        _label.Text = text;
        _label.ForeColor = color ?? Color.White;

        // Resize height if text is long
        using var g = CreateGraphics();
        var measured = g.MeasureString(text, _label.Font, Width - Padding.Horizontal);
        var newHeight = Math.Max(50, (int)measured.Height + Padding.Vertical + 8);
        Size = new Size(350, newHeight);
        PositionOnScreen();

        _hideTimer.Stop();
        _hideTimer.Interval = durationMs;
        _hideTimer.Start();

        if (!Visible) Show();
        else Refresh();
    }
}
