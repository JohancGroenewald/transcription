using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

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
    private const int CornerRadius = 14;
    private const int FadeTickIntervalMs = 40;
    private const int FadeDurationMs = 520;
    private const int CountdownTickIntervalMs = 40;
    private const int CountdownBarHeight = 4;
    private const int CountdownBarBottomMargin = 7;
    private static readonly Color CountdownTrackColor = Color.FromArgb(84, 30, 52, 40);

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private readonly Label _label;
    private readonly System.Windows.Forms.Timer _hideTimer;
    private readonly System.Windows.Forms.Timer _fadeTimer;
    private readonly System.Windows.Forms.Timer _countdownTimer;
    private int _overlayWidthPercent = AppConfig.DefaultOverlayWidthPercent;
    private int _overlayFontSizePt = AppConfig.DefaultOverlayFontSizePt;
    private bool _showOverlayBorder = true;
    private double _baseOpacity = AppConfig.DefaultOverlayOpacityPercent / 100.0;
    private int _lastDurationMs = 3000;
    private bool _lastShowCountdownBar;
    private bool _lastTapToCancel;
    private Color? _lastCountdownAccentColor;
    private bool _showCountdownBar;
    private int _countdownMessageId;
    private DateTime _countdownStartUtc;
    private int _countdownTotalMs;
    private Color? _countdownAccentColor;
    private bool _tapToCancelEnabled;
    private int _tapToCancelMessageId;
    // Message IDs prevent stale hide/fade timer ticks from resurfacing previous text.
    private int _activeMessageId;
    private int _hideTimerMessageId;
    private int _fadeMessageId;
    private ContentAlignment _lastTextAlign = ContentAlignment.MiddleCenter;
    private bool _lastCenterTextBlock;

    public event EventHandler<OverlayTappedEventArgs>? OverlayTapped;

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
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(12, 24, 18);
        ForeColor = DefaultTextColor;
        Opacity = _baseOpacity;
        Size = new Size(620, MinOverlayHeight);
        Padding = new Padding(18, 10, 18, 10);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", AppConfig.DefaultOverlayFontSizePt, FontStyle.Bold),
            ForeColor = DefaultTextColor,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false
        };
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _hideTimer.Tick += (s, e) => OnHideTimerTick();
        _fadeTimer = new System.Windows.Forms.Timer { Interval = FadeTickIntervalMs };
        _fadeTimer.Tick += (s, e) => OnFadeTick();
        _countdownTimer = new System.Windows.Forms.Timer { Interval = CountdownTickIntervalMs };
        _countdownTimer.Tick += (s, e) => OnCountdownTick();
        MouseClick += OnOverlayMouseClick;
        _label.MouseClick += OnOverlayMouseClick;

        Paint += OnOverlayPaint;
        ApplyHudSettings(
            AppConfig.DefaultOverlayOpacityPercent,
            AppConfig.DefaultOverlayWidthPercent,
            AppConfig.DefaultOverlayFontSizePt,
            showBorder: true);
        UpdateRoundedRegion();

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

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _hideTimer.Stop();
            _fadeTimer.Stop();
            Opacity = _baseOpacity;
            _hideTimerMessageId = 0;
            _fadeMessageId = 0;
            ResetCountdown();
            ResetTapToCancel();
        }
    }

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

    public int ShowMessage(
        string text,
        Color? color = null,
        int durationMs = 3000,
        ContentAlignment textAlign = ContentAlignment.MiddleCenter,
        bool centerTextBlock = false,
        bool showCountdownBar = false,
        bool tapToCancel = false,
        Color? countdownAccentColor = null)
    {
        if (InvokeRequired)
        {
            return (int)Invoke(new Func<int>(() => ShowMessage(
                text,
                color,
                durationMs,
                textAlign,
                centerTextBlock,
                showCountdownBar,
                tapToCancel,
                countdownAccentColor)));
        }

        var messageId = unchecked(++_activeMessageId);

        SuspendLayout();
        try
        {
            _label.Text = text;
            _label.ForeColor = color ?? DefaultTextColor;
            _lastDurationMs = durationMs;
            _lastTextAlign = textAlign;
            _lastCenterTextBlock = centerTextBlock;
            _lastShowCountdownBar = showCountdownBar;
            _lastTapToCancel = tapToCancel;
            _lastCountdownAccentColor = countdownAccentColor;

            var workingArea = GetTargetScreen().WorkingArea;
            var preferredWidth = Math.Clamp(
                (int)(workingArea.Width * (_overlayWidthPercent / 100.0)),
                MinOverlayWidth,
                MaxOverlayWidth);
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
            ConfigureLabelLayout(measured, textAlign, centerTextBlock);
            PositionOnScreen(workingArea);

            _fadeTimer.Stop();
            _hideTimer.Stop();
            _fadeMessageId = 0;
            _hideTimerMessageId = 0;
            Opacity = _baseOpacity;
            ConfigureCountdown(showCountdownBar, durationMs, messageId, countdownAccentColor);
            ConfigureTapToCancel(tapToCancel, durationMs, messageId);
            if (durationMs > 0)
            {
                _hideTimerMessageId = messageId;
                _hideTimer.Interval = durationMs;
                _hideTimer.Start();
            }
        }
        finally
        {
            ResumeLayout(performLayout: true);
        }

        var wasVisible = Visible;
        if (!wasVisible)
        {
            // Reveal only after the new frame is painted to avoid stale-buffer flashes.
            Opacity = 0;
            Show();
        }

        Invalidate(invalidateChildren: true);
        Update();

        if (!wasVisible)
            Opacity = _baseOpacity;

        // Reassert topmost without activating when newly shown.
        if (!wasVisible)
            _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        return messageId;
    }

    public void ApplyHudSettings(int opacityPercent, int widthPercent, int fontSizePt, bool showBorder)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyHudSettings(opacityPercent, widthPercent, fontSizePt, showBorder));
            return;
        }

        _overlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent(widthPercent);
        _overlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt(fontSizePt);
        _showOverlayBorder = showBorder;
        _baseOpacity = AppConfig.NormalizeOverlayOpacityPercent(opacityPercent) / 100.0;
        Opacity = _baseOpacity;

        var oldFont = _label.Font;
        _label.Font = new Font("Consolas", _overlayFontSizePt, FontStyle.Bold);
        oldFont.Dispose();

        if (Visible)
            ShowMessage(
                _label.Text,
                _label.ForeColor,
                _lastDurationMs,
                _lastTextAlign,
                _lastCenterTextBlock,
                _lastShowCountdownBar,
                _lastTapToCancel,
                _lastCountdownAccentColor);
    }

    private void ConfigureLabelLayout(Size measuredTextSize, ContentAlignment textAlign, bool centerTextBlock)
    {
        if (!centerTextBlock)
        {
            _label.Dock = DockStyle.Fill;
            _label.TextAlign = textAlign;
            return;
        }

        _label.Dock = DockStyle.None;
        var maxLabelWidth = Math.Max(40, ClientSize.Width - Padding.Horizontal);
        var maxLabelHeight = Math.Max(20, ClientSize.Height - Padding.Vertical);
        var labelWidth = Math.Clamp(measuredTextSize.Width, 1, maxLabelWidth);
        var labelHeight = Math.Clamp(measuredTextSize.Height, 1, maxLabelHeight);
        var left = Math.Max(Padding.Left, (ClientSize.Width - labelWidth) / 2);
        var top = Math.Max(Padding.Top, (ClientSize.Height - labelHeight) / 2);
        _label.Bounds = new Rectangle(left, top, labelWidth, labelHeight);
        _label.TextAlign = ContentAlignment.TopLeft;
    }

    private void OnOverlayPaint(object? sender, PaintEventArgs e)
    {
        if (_showOverlayBorder)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(BorderColor, 1.2f);
            var border = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedRectanglePath(border, CornerRadius);
            e.Graphics.DrawPath(pen, path);
        }

        if (!TryGetCountdownProgress(out var remainingFraction))
            return;

        var trackMargin = Math.Max(8, Padding.Left);
        var trackWidth = Math.Max(80, Width - (trackMargin * 2));
        var trackTop = Math.Max(2, Height - CountdownBarBottomMargin - CountdownBarHeight);
        var trackBounds = new Rectangle(trackMargin, trackTop, trackWidth, CountdownBarHeight);
        var fillWidth = (int)Math.Round(trackBounds.Width * remainingFraction);

        using var trackBrush = new SolidBrush(CountdownTrackColor);
        e.Graphics.FillRectangle(trackBrush, trackBounds);

        if (fillWidth > 0)
        {
            var fillBounds = new Rectangle(trackBounds.Left, trackBounds.Top, fillWidth, trackBounds.Height);
            var fillColor = Color.FromArgb(220, _countdownAccentColor ?? _label.ForeColor);
            using var fillBrush = new SolidBrush(fillColor);
            e.Graphics.FillRectangle(fillBrush, fillBounds);
        }
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
            return;

        using var path = CreateRoundedRectanglePath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private void OnHideTimerTick()
    {
        _hideTimer.Stop();

        if (!Visible)
            return;

        if (_hideTimerMessageId == 0 || _hideTimerMessageId != _activeMessageId)
            return;

        BeginFadeOut(_hideTimerMessageId);
    }

    private void BeginFadeOut(int messageId)
    {
        if (!Visible)
            return;

        _fadeMessageId = messageId;
        _fadeTimer.Stop();
        _fadeTimer.Start();
    }

    private void OnFadeTick()
    {
        if (_fadeMessageId == 0 || _fadeMessageId != _activeMessageId)
        {
            _fadeTimer.Stop();
            return;
        }

        var steps = Math.Max(1, FadeDurationMs / FadeTickIntervalMs);
        var nextOpacity = Opacity - (_baseOpacity / steps);
        if (nextOpacity <= 0.02)
        {
            _fadeTimer.Stop();
            _fadeMessageId = 0;
            _label.Text = string.Empty;
            Hide();
            return;
        }

        Opacity = nextOpacity;
        Invalidate();
    }

    private void ConfigureCountdown(bool showCountdownBar, int durationMs, int messageId, Color? countdownAccentColor)
    {
        if (!showCountdownBar || durationMs <= 0)
        {
            ResetCountdown();
            return;
        }

        _showCountdownBar = true;
        _countdownMessageId = messageId;
        _countdownStartUtc = DateTime.UtcNow;
        _countdownTotalMs = durationMs + FadeDurationMs;
        _countdownAccentColor = countdownAccentColor;
        _countdownTimer.Start();
    }

    private void OnCountdownTick()
    {
        if (!_showCountdownBar || !Visible)
        {
            _countdownTimer.Stop();
            return;
        }

        if (!TryGetCountdownProgress(out var remainingFraction) || remainingFraction <= 0)
        {
            ResetCountdown();
            Invalidate();
            return;
        }

        Invalidate();
    }

    private void ConfigureTapToCancel(bool tapToCancel, int durationMs, int messageId)
    {
        if (!tapToCancel || durationMs <= 0)
        {
            ResetTapToCancel();
            return;
        }

        _tapToCancelEnabled = true;
        _tapToCancelMessageId = messageId;
        Cursor = Cursors.Hand;
        _label.Cursor = Cursors.Hand;
    }

    private bool TryGetCountdownProgress(out double remainingFraction)
    {
        remainingFraction = 0;
        if (!_showCountdownBar || _countdownMessageId == 0 || _countdownMessageId != _activeMessageId)
            return false;

        if (_countdownTotalMs <= 0)
            return false;

        var elapsedMs = (DateTime.UtcNow - _countdownStartUtc).TotalMilliseconds;
        var remaining = 1.0 - (elapsedMs / _countdownTotalMs);
        remainingFraction = Math.Clamp(remaining, 0, 1);
        return true;
    }

    private void ResetCountdown()
    {
        _countdownTimer.Stop();
        _showCountdownBar = false;
        _countdownMessageId = 0;
        _countdownTotalMs = 0;
        _countdownAccentColor = null;
    }

    public void SetCountdownAccentColor(Color? color)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetCountdownAccentColor(color));
            return;
        }

        _countdownAccentColor = color;
        _lastCountdownAccentColor = color;
        if (_showCountdownBar && Visible)
            Invalidate();
    }

    private void ResetTapToCancel()
    {
        _tapToCancelEnabled = false;
        _tapToCancelMessageId = 0;
        Cursor = Cursors.Default;
        _label.Cursor = Cursors.Default;
    }

    private void OnOverlayMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (!_tapToCancelEnabled)
            return;

        if (_tapToCancelMessageId == 0 || _tapToCancelMessageId != _activeMessageId)
            return;

        var messageId = _tapToCancelMessageId;
        ResetTapToCancel();
        OverlayTapped?.Invoke(this, new OverlayTappedEventArgs(messageId));
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        if (diameter <= 2)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public sealed class OverlayTappedEventArgs : EventArgs
{
    public OverlayTappedEventArgs(int messageId)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }
}
