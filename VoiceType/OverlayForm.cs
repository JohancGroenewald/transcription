using System.Runtime.InteropServices;

namespace VoiceType;

public class OverlayForm : Form
{
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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
        var screen = GetTargetScreen().WorkingArea;
        Location = new Point(
            screen.Right - Width - 16,
            screen.Bottom - Height - 16);
    }

    private static Screen GetTargetScreen()
    {
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && GetWindowRect(foreground, out var rect))
        {
            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!bounds.IsEmpty)
                return Screen.FromRectangle(bounds);
        }

        return Screen.FromPoint(Cursor.Position);
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

        if (!Visible)
            Show();
        else
            Refresh();

        // Reassert topmost without activating so notifications stay visible.
        _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }
}
