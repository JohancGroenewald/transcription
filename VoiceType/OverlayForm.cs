using System.Runtime.InteropServices;

namespace VoiceType;

public class OverlayForm : Form
{
    private static readonly Color DefaultTextColor = Color.FromArgb(174, 255, 188);
    private static readonly Color BorderColor = Color.FromArgb(120, 126, 255, 191);
    private const int BottomOffset = 18;
    private const int HorizontalMargin = 20;
    private const int MinOverlayWidth = 460;
    private const int MaxOverlayWidth = 980;
    private const int MinOverlayHeight = 58;

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
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(12, 24, 18);
        ForeColor = DefaultTextColor;
        Opacity = 0.94;
        Size = new Size(620, MinOverlayHeight);
        Padding = new Padding(18, 10, 18, 10);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 12f, FontStyle.Bold),
            ForeColor = DefaultTextColor,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _hideTimer.Tick += (s, e) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        Paint += OnOverlayPaint;

        // Position: bottom-center, above taskbar
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
        PositionOnScreen(GetTargetScreen().WorkingArea);
    }

    private void PositionOnScreen(Rectangle screen)
    {
        Location = new Point(
            screen.Left + ((screen.Width - Width) / 2),
            screen.Bottom - Height - BottomOffset);
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
        _label.ForeColor = color ?? DefaultTextColor;

        var workingArea = GetTargetScreen().WorkingArea;
        var preferredWidth = Math.Clamp((int)(workingArea.Width * 0.62), MinOverlayWidth, MaxOverlayWidth);
        var width = Math.Min(preferredWidth, workingArea.Width - HorizontalMargin * 2);
        if (width < 260)
            width = Math.Max(220, workingArea.Width - 12);

        var measured = TextRenderer.MeasureText(
            text,
            _label.Font,
            new Size(width - Padding.Horizontal, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var height = Math.Max(MinOverlayHeight, measured.Height + Padding.Vertical + 8);

        Size = new Size(width, height);
        PositionOnScreen(workingArea);

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

    private void OnOverlayPaint(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(BorderColor, 1.2f);
        var border = new Rectangle(0, 0, Width - 1, Height - 1);
        e.Graphics.DrawRectangle(pen, border);
    }
}
