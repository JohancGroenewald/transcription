using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;

namespace VoiceType;

public class OverlayForm : Form
{
    private static readonly Color DefaultTextColor = Color.FromArgb(174, 255, 188);
    private static readonly Color BorderColor = Color.FromArgb(120, 126, 255, 191);
    private static readonly Color ActionTextColor = Color.FromArgb(246, 229, 159);
    private const int BottomOffset = 18;
    private const int HorizontalMargin = 20;
    private const int MinOverlayWidth = 460;
    private const int MaxOverlayWidth = 980;
    private const int MinOverlayHeight = 58;
    private const int CornerRadius = 14;
    private const int ActionLineSpacing = 4;
    private const int DefaultFadeTickIntervalMs = 40;
    private const int DefaultFadeDurationMs = 520;
    private const int CountdownTickIntervalMs = 40;
    private const int CountdownBarHeight = 4;
    private const int CountdownBarBottomMargin = 7;
    private static readonly Color TransparentOverlayBackgroundColor = Color.Fuchsia;
    private static readonly Color CountdownTrackColor = Color.FromArgb(84, 30, 52, 40);

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new(-2);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private readonly Label _label;
    private readonly Label _actionLabel;
    private readonly Label _prefixLabel;
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
    private bool _lastShowActionLine;
    private bool _lastShowPrefixLine;
    private bool _showCountdownBar;
    private string _lastActionText = string.Empty;
    private Color _lastActionColor = ActionTextColor;
    private string _lastPrefixText = string.Empty;
    private Color _lastPrefixColor = DefaultTextColor;
    private int _countdownMessageId;
    private DateTime _countdownStartUtc;
    private int _countdownTotalMs;
    private bool _tapToCancelEnabled;
    private int _tapToCancelMessageId;
    // Message IDs prevent stale hide/fade timer ticks from resurfacing previous text.
    private int _activeMessageId;
    private int _hideTimerMessageId;
    private int _fadeMessageId;
    private int _fadeDurationMs = DefaultFadeDurationMs;
    private int _fadeTickIntervalMs = DefaultFadeTickIntervalMs;
    private bool _animateOnAutoHide;
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
        BackColor = TransparentOverlayBackgroundColor;
        TransparencyKey = TransparentOverlayBackgroundColor;
        ForeColor = DefaultTextColor;
        Opacity = _baseOpacity;
        Size = new Size(620, MinOverlayHeight);
        Padding = new Padding(18, 10, 18, 10);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", AppConfig.DefaultOverlayFontSizePt, FontStyle.Bold),
            ForeColor = DefaultTextColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false
        };
        _actionLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Font = new Font("Consolas", Math.Max(9, AppConfig.DefaultOverlayFontSizePt - 2), FontStyle.Regular),
            ForeColor = ActionTextColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = false,
            AutoSize = false,
            Visible = false
        };
        _prefixLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Font = new Font("Consolas", Math.Max(8, AppConfig.DefaultOverlayFontSizePt - 3), FontStyle.Regular),
            ForeColor = Color.FromArgb(150, 173, 255, 173),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = false,
            AutoSize = false,
            Visible = false
        };
        Controls.Add(_actionLabel);
        Controls.Add(_prefixLabel);
        Controls.Add(_label);

        _hideTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _hideTimer.Tick += (s, e) => OnHideTimerTick();
        _fadeTimer = new System.Windows.Forms.Timer { Interval = _fadeTickIntervalMs };
        _fadeTimer.Tick += (s, e) => OnFadeTick();
        _countdownTimer = new System.Windows.Forms.Timer { Interval = CountdownTickIntervalMs };
        _countdownTimer.Tick += (s, e) => OnCountdownTick();
        MouseClick += OnOverlayMouseClick;
        _label.MouseClick += OnOverlayMouseClick;
        _actionLabel.MouseClick += OnOverlayMouseClick;
        _prefixLabel.MouseClick += OnOverlayMouseClick;

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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Keep overlays visually transparent: render only the content and optional border/bar.
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _hideTimer.Stop();
            _fadeTimer.Stop();
            _countdownTimer.Stop();
            Opacity = _baseOpacity;
            _hideTimerMessageId = 0;
            _fadeMessageId = 0;
            ResetCountdown();
            _actionLabel.Text = string.Empty;
            _actionLabel.Visible = false;
            _lastActionText = string.Empty;
            _lastShowActionLine = false;
            _prefixLabel.Text = string.Empty;
            _prefixLabel.Visible = false;
            _lastPrefixText = string.Empty;
            _lastShowPrefixLine = false;
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
        string? actionText = null,
        Color? actionColor = null,
        string? prefixText = null,
        Color? prefixColor = null,
        bool autoPosition = true,
        bool animateOnHide = false)
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
                actionText,
                actionColor,
                prefixText,
                prefixColor,
                autoPosition,
                animateOnHide)));
        }

        var messageId = unchecked(++_activeMessageId);

        SuspendLayout();
        try
        {
            _label.Text = text;
            _label.ForeColor = color ?? DefaultTextColor;
            _lastActionText = string.IsNullOrWhiteSpace(actionText) ? string.Empty : actionText;
            _lastActionColor = actionColor ?? ActionTextColor;
            _lastShowActionLine = !string.IsNullOrWhiteSpace(_lastActionText);
            _lastPrefixText = string.IsNullOrWhiteSpace(prefixText) ? string.Empty : prefixText;
            _lastPrefixColor = prefixColor ?? DefaultTextColor;
            _lastShowPrefixLine = !string.IsNullOrWhiteSpace(_lastPrefixText);
            _lastDurationMs = durationMs;
            _lastTextAlign = textAlign;
            _lastCenterTextBlock = centerTextBlock;
            _lastShowCountdownBar = showCountdownBar;
            _lastTapToCancel = tapToCancel;
            _animateOnAutoHide = animateOnHide;

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
            var actionSize = Size.Empty;
            var actionLineHeight = 0;
            if (_lastShowActionLine)
            {
                _actionLabel.Text = _lastActionText;
                _actionLabel.ForeColor = _lastActionColor;
                _actionLabel.Visible = true;
                actionSize = TextRenderer.MeasureText(
                    _lastActionText,
                    _actionLabel.Font,
                    new Size(width - Padding.Horizontal, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                actionLineHeight = Math.Max(18, actionSize.Height + 2);
            }
            else
            {
                _actionLabel.Text = string.Empty;
                _actionLabel.Visible = false;
            }

            var prefixSize = Size.Empty;
            var prefixLineHeight = 0;
            if (_lastShowPrefixLine)
            {
                _prefixLabel.Text = _lastPrefixText;
                _prefixLabel.ForeColor = _lastPrefixColor;
                _prefixLabel.Visible = true;
                prefixSize = TextRenderer.MeasureText(
                    _lastPrefixText,
                    _prefixLabel.Font,
                    new Size(width - Padding.Horizontal, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                prefixLineHeight = Math.Max(16, prefixSize.Height + 2);
            }
            else
            {
                _prefixLabel.Text = string.Empty;
                _prefixLabel.Visible = false;
            }

            var height = Math.Max(
                MinOverlayHeight,
                measured.Height +
                (actionLineHeight > 0 ? actionLineHeight + ActionLineSpacing : 0) +
                (prefixLineHeight > 0 ? prefixLineHeight + ActionLineSpacing : 0) +
                Padding.Vertical +
                8);

            Size = new Size(width, height);
            ConfigureLabelLayout(
                measured,
                actionLineHeight,
                prefixLineHeight,
                textAlign,
                centerTextBlock,
                _lastShowActionLine,
                _lastShowPrefixLine);
            if (autoPosition)
                PositionOnScreen(workingArea);

            Opacity = _baseOpacity;
            _countdownTimer.Stop();
            _hideTimer.Stop();
            _fadeTimer.Stop();
            _hideTimerMessageId = 0;
            _fadeMessageId = 0;
            ConfigureCountdown(showCountdownBar, durationMs, messageId);
            ConfigureTapToCancel(tapToCancel, messageId);
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
        var oldActionFont = _actionLabel.Font;
        _actionLabel.Font = new Font("Consolas", Math.Max(9, _overlayFontSizePt - 2), FontStyle.Regular);
        oldActionFont.Dispose();
        var oldPrefixFont = _prefixLabel.Font;
        _prefixLabel.Font = new Font("Consolas", Math.Max(8, _overlayFontSizePt - 3), FontStyle.Regular);
        oldPrefixFont.Dispose();

        if (Visible)
            ShowMessage(
                _label.Text,
                _label.ForeColor,
                _lastDurationMs,
                _lastTextAlign,
                _lastCenterTextBlock,
                _lastShowCountdownBar,
                _lastTapToCancel,
                _lastActionText,
                _lastActionColor,
                _lastPrefixText,
                _lastPrefixColor);
    }

    public void PromoteToTopmost()
    {
        if (InvokeRequired)
        {
            Invoke((Action)PromoteToTopmost);
            return;
        }

        if (!IsHandleCreated || IsDisposed)
            return;

        _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void DemoteFromTopmost()
    {
        if (InvokeRequired)
        {
            Invoke((Action)DemoteFromTopmost);
            return;
        }

        if (!IsHandleCreated || IsDisposed)
            return;

        TopMost = false;
        _ = SetWindowPos(Handle, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    public void FadeOut(int delayMilliseconds = 0, int? fadeDurationMs = null, int? fadeTickIntervalMs = null)
    {
        var delayMs = Math.Max(0, delayMilliseconds);
        ConfigureFadeTiming(fadeDurationMs, fadeTickIntervalMs);
        var messageId = _activeMessageId;
        if (delayMs <= 0)
        {
            if (InvokeRequired)
            {
                Invoke((Action)(() => FadeOut(0)));
                return;
            }

            BeginFadeOut(messageId, force: true);
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
            if (IsDisposed || IsHandleCreated == false || !Visible)
                return;

            if (_activeMessageId != messageId)
                return;

            if (InvokeRequired)
            {
                Invoke((Action)(() => BeginFadeOut(messageId, force: true)));
                return;
            }

            BeginFadeOut(messageId, force: true);
        });
    }

    private void ConfigureLabelLayout(
        Size measuredTextSize,
        int measuredActionLineHeight,
        int measuredPrefixLineHeight,
        ContentAlignment textAlign,
        bool centerTextBlock,
        bool hasActionText,
        bool hasPrefixText)
    {
        _actionLabel.Visible = hasActionText;
        _prefixLabel.Visible = hasPrefixText;
        _label.Dock = DockStyle.None;
        _actionLabel.Dock = DockStyle.None;
        _prefixLabel.Dock = DockStyle.None;
        _actionLabel.TextAlign = ContentAlignment.TopLeft;
        _prefixLabel.TextAlign = ContentAlignment.TopLeft;
        var actionLineHeight = Math.Max(0, measuredActionLineHeight);
        var prefixLineHeight = Math.Max(0, measuredPrefixLineHeight);
        var metaAreaHeight = actionLineHeight + prefixLineHeight + (hasActionText ? ActionLineSpacing : 0)
            + (hasPrefixText ? ActionLineSpacing : 0);

        var cursorY = Padding.Top;

        if (_lastShowActionLine)
        {
            _actionLabel.Bounds = new Rectangle(
                Padding.Left,
                cursorY,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                actionLineHeight);
            cursorY += actionLineHeight + ActionLineSpacing;
        }
        else
        {
            _actionLabel.Text = string.Empty;
            _actionLabel.Visible = false;
        }

        if (hasPrefixText)
        {
            _prefixLabel.Bounds = new Rectangle(
                Padding.Left,
                cursorY,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                prefixLineHeight);
            cursorY += prefixLineHeight + ActionLineSpacing;
        }

        var availableHeight = Math.Max(
            20,
            ClientSize.Height - Padding.Vertical - cursorY + Padding.Top);

        if (!centerTextBlock)
        {
            _actionLabel.Width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            _actionLabel.Height = actionLineHeight;
            _prefixLabel.Width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
            _prefixLabel.Height = prefixLineHeight;
            _label.Bounds = new Rectangle(
                Padding.Left,
                cursorY,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                Math.Max(20, availableHeight));
            _label.TextAlign = textAlign;
            _actionLabel.BringToFront();
            return;
        }

        if (!hasActionText && !hasPrefixText)
        {
            _label.TextAlign = textAlign;
            return;
        }

        var maxLabelWidth = Math.Max(40, ClientSize.Width - Padding.Horizontal);
        var maxLabelHeight = Math.Max(20, ClientSize.Height - Padding.Vertical - metaAreaHeight);
        var labelWidth = Math.Clamp(measuredTextSize.Width, 1, maxLabelWidth);
        var labelHeight = Math.Clamp(measuredTextSize.Height, 1, maxLabelHeight);
        var left = Math.Max(Padding.Left, (ClientSize.Width - labelWidth) / 2);
        var top = Math.Max(Padding.Top, (ClientSize.Height - metaAreaHeight - labelHeight) / 2);
        _label.Bounds = new Rectangle(left, top, labelWidth, labelHeight);
        _label.TextAlign = ContentAlignment.TopLeft;
    }

    private void OnOverlayPaint(object? sender, PaintEventArgs e)
    {
        // Intentionally no background fill so the overlays render as floating transparent text-only popups.
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

        Hide();
    }

    private void BeginFadeOut(int messageId, bool force = false)
    {
        if (!Visible || _activeMessageId != messageId)
            return;

        if (!force && !_animateOnAutoHide)
        {
            Hide();
            return;
        }

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

        if (_baseOpacity <= 0)
        {
            _fadeTimer.Stop();
            _fadeMessageId = 0;
            _label.Text = string.Empty;
            _actionLabel.Text = string.Empty;
            _actionLabel.Visible = false;
            _lastShowActionLine = false;
            _prefixLabel.Text = string.Empty;
            _prefixLabel.Visible = false;
            _lastShowPrefixLine = false;
            _countdownMessageId = 0;
            _countdownTotalMs = 0;
            Hide();
            return;
        }

        var steps = Math.Max(1, _fadeDurationMs / Math.Max(1, _fadeTickIntervalMs));
        var nextOpacity = Opacity - (_baseOpacity / steps);
        if (nextOpacity <= 0.02)
        {
            _fadeTimer.Stop();
            _fadeMessageId = 0;
            _label.Text = string.Empty;
            _actionLabel.Text = string.Empty;
            _actionLabel.Visible = false;
            _lastShowActionLine = false;
            _prefixLabel.Text = string.Empty;
            _prefixLabel.Visible = false;
            _lastShowPrefixLine = false;
            _countdownMessageId = 0;
            _countdownTotalMs = 0;
            Hide();
            return;
        }

        Opacity = nextOpacity;
        Invalidate();
    }

    private void ConfigureCountdown(bool showCountdownBar, int durationMs, int messageId)
    {
        if (!showCountdownBar || durationMs <= 0)
        {
            ResetCountdown();
            return;
        }

        _showCountdownBar = true;
        _countdownMessageId = messageId;
        _countdownStartUtc = DateTime.UtcNow;
        _countdownTotalMs = durationMs + _fadeDurationMs;
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

    private void ConfigureTapToCancel(bool tapToCancel, int messageId)
    {
        if (!tapToCancel)
        {
            ResetTapToCancel();
            return;
        }

        _tapToCancelEnabled = true;
        _tapToCancelMessageId = messageId;
        Cursor = Cursors.Hand;
        _label.Cursor = Cursors.Hand;
        _actionLabel.Cursor = Cursors.Hand;
        _prefixLabel.Cursor = Cursors.Hand;
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
    }

    private void ResetTapToCancel()
    {
        _tapToCancelEnabled = false;
        _tapToCancelMessageId = 0;
        Cursor = Cursors.Default;
        _label.Cursor = Cursors.Default;
        _actionLabel.Cursor = Cursors.Default;
        _prefixLabel.Cursor = Cursors.Default;
    }

    private void ConfigureFadeTiming(int? fadeDurationMs, int? fadeTickIntervalMs)
    {
        var profileDurationMs = fadeDurationMs ?? DefaultFadeDurationMs;
        var profileTickMs = fadeTickIntervalMs ?? DefaultFadeTickIntervalMs;

        _fadeDurationMs = Math.Max(1, profileDurationMs);
        _fadeTickIntervalMs = Math.Clamp(profileTickMs, 8, 200);
        _fadeTimer.Interval = _fadeTickIntervalMs;
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
