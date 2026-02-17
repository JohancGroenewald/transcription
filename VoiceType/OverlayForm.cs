using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VoiceType;

public class OverlayForm : Form
{
    private static readonly Color DefaultTextColor = Color.FromArgb(255, 255, 188);
    private const string OverlayFontFamily = "Segoe UI";
    private static readonly Color BorderColor = Color.FromArgb(120, 126, 255, 191);
    private static readonly Color ActionTextColor = Color.FromArgb(255, 229, 159);
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
    private const int CountdownBarHeight = 8;
    private const int CountdownBarBottomMargin = 10;
    private const int CountdownBarAreaPadding = 1;
    private const int ListeningMeterWidth = 230;
    private const int ListeningMeterHeight = 26;
    private const int ListeningMeterTopOffsetPx = 200;
    private const int ListeningMeterBarCount = 8;
    private const int ListeningMeterBarSpacing = 2;
    private const int ListeningMeterActiveBarBaseAlpha = 150;
    private const int ListeningMeterInactiveBarAlpha = 55;
    private static readonly Color ListeningMeterActiveColor = Color.FromArgb(200, 170, 255, 170);
    private static readonly Color ListeningMeterInactiveColor = Color.FromArgb(ListeningMeterInactiveBarAlpha, 180, 180, 180);
    private static readonly Color TransparentOverlayBackgroundColor = Color.FromArgb(255, 240, 240, 240);
    private const float CopyTapBorderWidth = 3.0f;
    private const int CopyTapBorderAlpha = 255;
    private const int CountdownPlaybackIconGapPx = 10;
    private const string HideStackIconGlyph = "×";
    private const int HideStackIconPaddingPx = 18;

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 0x0001;
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
    private bool _showListeningLevelMeter;
    private int _listeningLevelPercent;
    private string _lastActionText = string.Empty;
    private Color _lastActionColor = ActionTextColor;
    private string _lastPrefixText = string.Empty;
    private Color _lastPrefixColor = DefaultTextColor;
    private string _copyText = string.Empty;
    private string _countdownPlaybackIcon = string.Empty;
    private string _lastCountdownPlaybackIcon = string.Empty;
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
    private bool _lastUseFullWidthText;
    private bool _lastCenterTextBlock;
    private bool _isHorizontalDragging;
    private bool _dragStarted;
    private int _lastDragScreenX;
    private bool _ignoreNextClickAfterDrag;
    private bool _pressInProgress;
    private bool _tapHandledForCurrentPress;
    private const int HorizontalDragActivationThreshold = 1;
    private bool _showCopyTapFeedbackBorder;
    private Color _activeBorderColor = BorderColor;
    private bool _allowCopyTap = true;
    private Rectangle _countdownPlaybackIconBounds = Rectangle.Empty;
    private bool _showHideStackIcon;
    private Rectangle _hideStackIconBounds = Rectangle.Empty;

    public event EventHandler<OverlayTappedEventArgs>? OverlayTapped;
    public event EventHandler<OverlayCopyTappedEventArgs>? OverlayCopyTapped;
    public event EventHandler<OverlayHorizontalDraggedEventArgs>? OverlayHorizontalDragged;
    public event EventHandler<OverlayCountdownPlaybackIconTappedEventArgs>? OverlayCountdownPlaybackIconTapped;
    public event EventHandler<OverlayHideStackIconTappedEventArgs>? OverlayHideStackIconTapped;

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
        Opacity = 1.0;
        Size = new Size(620, MinOverlayHeight);
        Padding = new Padding(18, 10, 18, 10);

        _label = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(OverlayFontFamily, AppConfig.DefaultOverlayFontSizePt + 1, FontStyle.Bold),
            ForeColor = DefaultTextColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            UseCompatibleTextRendering = false
        };
        _actionLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Font = new Font(OverlayFontFamily, Math.Max(10, AppConfig.DefaultOverlayFontSizePt - 1), FontStyle.Bold),
            ForeColor = ActionTextColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            AutoSize = false,
            Visible = false,
            UseCompatibleTextRendering = false
        };
        _prefixLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Font = new Font(OverlayFontFamily, Math.Max(10, AppConfig.DefaultOverlayFontSizePt - 2), FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 173, 255, 173),
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            AutoSize = false,
            Visible = false,
            UseCompatibleTextRendering = false
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
        RegisterDragHandlers(this);
        MouseCaptureChanged += OnMouseCaptureChanged;

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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = (IntPtr)HTCLIENT;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(TransparentOverlayBackgroundColor);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
        {
            _hideTimer.Stop();
            _fadeTimer.Stop();
            _countdownTimer.Stop();
            Opacity = 1.0;
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
            _showListeningLevelMeter = false;
            _listeningLevelPercent = 0;
            _copyText = string.Empty;
            _countdownPlaybackIcon = string.Empty;
            _lastCountdownPlaybackIcon = string.Empty;
            _countdownPlaybackIconBounds = Rectangle.Empty;
            _showHideStackIcon = false;
            _hideStackIconBounds = Rectangle.Empty;
            _showCopyTapFeedbackBorder = false;
            _activeBorderColor = BorderColor;
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
        bool animateOnHide = false,
        bool autoHide = false,
        bool showListeningLevelMeter = false,
        int listeningLevelPercent = 0,
        bool allowCopyTap = true,
        string? copyText = null,
        string? countdownPlaybackIcon = null,
        bool fullWidthText = false,
        bool showHideStackIcon = false)
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
                animateOnHide,
                autoHide,
                showListeningLevelMeter,
                listeningLevelPercent,
                allowCopyTap,
                copyText,
                countdownPlaybackIcon,
                fullWidthText,
                showHideStackIcon)));
        }

        var messageId = unchecked(++_activeMessageId);
        var resolvedCopyText = string.IsNullOrWhiteSpace(copyText) ? text : copyText;

        SuspendLayout();
        try
        {
            _label.Text = text;
            _copyText = resolvedCopyText ?? string.Empty;
            _label.ForeColor = EnsureOpaque(color ?? DefaultTextColor);
            _countdownPlaybackIcon = string.IsNullOrWhiteSpace(countdownPlaybackIcon)
                ? string.Empty
                : countdownPlaybackIcon;
            _showHideStackIcon = showHideStackIcon;
            _lastActionText = string.IsNullOrWhiteSpace(actionText) ? string.Empty : actionText;
            _lastActionColor = EnsureOpaque(actionColor ?? ActionTextColor);
            _lastShowActionLine = !string.IsNullOrWhiteSpace(_lastActionText);
            _lastPrefixText = string.IsNullOrWhiteSpace(prefixText) ? string.Empty : prefixText;
            _lastPrefixColor = EnsureOpaque(prefixColor ?? DefaultTextColor);
            _lastShowPrefixLine = !string.IsNullOrWhiteSpace(_lastPrefixText);
            _lastDurationMs = durationMs;
            _lastTextAlign = textAlign;
            _lastCenterTextBlock = centerTextBlock;
            _lastShowCountdownBar = showCountdownBar;
            _lastTapToCancel = tapToCancel;
            _animateOnAutoHide = animateOnHide;
            _showListeningLevelMeter = showListeningLevelMeter;
            _listeningLevelPercent = Math.Clamp(listeningLevelPercent, 0, 100);
            _allowCopyTap = allowCopyTap;
            _hideStackIconBounds = Rectangle.Empty;
            _lastUseFullWidthText = fullWidthText;
            _lastCountdownPlaybackIcon = _countdownPlaybackIcon;

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
                8 +
                (_lastShowCountdownBar ? GetCountdownBarReservedHeight() : 0));

            Size = new Size(width, height);
            ConfigureLabelLayout(
                measured,
                actionLineHeight,
                prefixLineHeight,
                textAlign,
                fullWidthText,
                centerTextBlock,
                _lastShowActionLine,
                _lastShowPrefixLine);
            if (autoPosition)
                PositionOnScreen(workingArea);

            Opacity = 1.0;
            _countdownTimer.Stop();
            _hideTimer.Stop();
            _fadeTimer.Stop();
            _hideTimerMessageId = 0;
            _fadeMessageId = 0;
            ConfigureCountdown(showCountdownBar, durationMs, messageId);
            ConfigureTapToCancel(tapToCancel, messageId);
            if (autoHide && durationMs > 0)
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
            Opacity = 1.0;

        // Reassert topmost without activating when newly shown.
        if (!wasVisible)
            _ = SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        return messageId;
    }

    public void ApplyCountdownPlaybackIcon(string? countdownPlaybackIcon)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyCountdownPlaybackIcon(countdownPlaybackIcon)));
            return;
        }

        _countdownPlaybackIcon = string.IsNullOrWhiteSpace(countdownPlaybackIcon)
            ? string.Empty
            : countdownPlaybackIcon;
        _lastCountdownPlaybackIcon = _countdownPlaybackIcon;
        _countdownPlaybackIconBounds = Rectangle.Empty;
        if (_countdownPlaybackIcon is not null && Visible)
            Invalidate();
    }

    public void ClearCountdownBar()
    {
        if (InvokeRequired)
        {
            Invoke((Action)ClearCountdownBar);
            return;
        }

        ResetCountdown();
        Invalidate();
    }

    public void ApplyHudSettings(int opacityPercent, int widthPercent, int fontSizePt, bool showBorder)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyHudSettings(opacityPercent, widthPercent, fontSizePt, showBorder));
            return;
        }

        _overlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent(widthPercent);
        _overlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt(Math.Min(AppConfig.MaxOverlayFontSizePt, fontSizePt + 3));
        _showOverlayBorder = showBorder;
        _baseOpacity = AppConfig.NormalizeOverlayOpacityPercent(opacityPercent) / 100.0;
        Opacity = 1.0;

        var oldFont = _label.Font;
        _label.Font = new Font(OverlayFontFamily, _overlayFontSizePt, FontStyle.Bold);
        oldFont.Dispose();
        var oldActionFont = _actionLabel.Font;
        _actionLabel.Font = new Font(OverlayFontFamily, Math.Max(10, _overlayFontSizePt - 2), FontStyle.Bold);
        oldActionFont.Dispose();
        var oldPrefixFont = _prefixLabel.Font;
        _prefixLabel.Font = new Font(OverlayFontFamily, Math.Max(10, _overlayFontSizePt - 2), FontStyle.Bold);
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
                _lastPrefixColor,
                countdownPlaybackIcon: _lastCountdownPlaybackIcon,
                fullWidthText: _lastUseFullWidthText,
                showHideStackIcon: _showHideStackIcon);
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
        bool fullWidthText,
        bool centerTextBlock,
        bool hasActionText,
        bool hasPrefixText)
    {
        _actionLabel.Visible = !fullWidthText && hasActionText;
        _prefixLabel.Visible = !fullWidthText && hasPrefixText;
        _label.Dock = DockStyle.None;
        _actionLabel.Dock = DockStyle.None;
        _prefixLabel.Dock = DockStyle.None;
        _label.TextAlign = ContentAlignment.MiddleCenter;
        _actionLabel.TextAlign = ContentAlignment.MiddleCenter;
        _prefixLabel.TextAlign = ContentAlignment.MiddleCenter;
        _label.Visible = !fullWidthText;
        var actionLineHeight = Math.Max(0, measuredActionLineHeight);
        var prefixLineHeight = Math.Max(0, measuredPrefixLineHeight);
        var reservedCountdownHeight = _lastShowCountdownBar ? GetCountdownBarReservedHeight() : 0;
        var metaAreaHeight = actionLineHeight + prefixLineHeight + (hasActionText ? ActionLineSpacing : 0)
            + (hasPrefixText ? ActionLineSpacing : 0);
        var hideStackIconReservePx = _showHideStackIcon
            ? GetHideStackIconReservePx()
            : 0;
        var contentLeft = Padding.Left + hideStackIconReservePx;
        var contentWidth = Math.Max(1, ClientSize.Width - Padding.Horizontal - hideStackIconReservePx);

        var cursorY = Padding.Top;

        if (_lastShowActionLine)
        {
            _actionLabel.Bounds = new Rectangle(
                contentLeft,
                cursorY,
                contentWidth,
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
                contentLeft,
                cursorY,
                contentWidth,
                prefixLineHeight);
            cursorY += prefixLineHeight + ActionLineSpacing;
        }

        var availableHeight = Math.Max(
            20,
            ClientSize.Height - Padding.Vertical - cursorY + Padding.Top - reservedCountdownHeight);

        if (!centerTextBlock)
        {
            _actionLabel.Width = contentWidth;
            _actionLabel.Height = actionLineHeight;
            _prefixLabel.Width = contentWidth;
            _prefixLabel.Height = prefixLineHeight;
            _label.Bounds = new Rectangle(
                contentLeft,
                cursorY,
                contentWidth,
                Math.Max(20, availableHeight));
            _label.TextAlign = ContentAlignment.MiddleCenter;
            _actionLabel.BringToFront();
            return;
        }

        if (!hasActionText && !hasPrefixText)
        {
            _label.TextAlign = ContentAlignment.MiddleCenter;
            return;
        }

        var maxLabelWidth = Math.Max(40, contentWidth);
        var maxLabelHeight = Math.Max(
            20,
            ClientSize.Height - Padding.Vertical - metaAreaHeight - reservedCountdownHeight);
        var labelWidth = Math.Clamp(measuredTextSize.Width, 1, maxLabelWidth);
        var labelHeight = Math.Clamp(measuredTextSize.Height, 1, maxLabelHeight);
        var contentRight = contentLeft + contentWidth;
        var left = Math.Max(contentLeft, Math.Min(contentRight - labelWidth, contentLeft + (contentWidth - labelWidth) / 2));
        var centeredAreaHeight = Math.Max(1, maxLabelHeight);
        var top = Math.Max(
            Padding.Top,
            Padding.Top + (centeredAreaHeight - labelHeight + ActionLineSpacing) / 2);
        _label.Bounds = new Rectangle(left, top, labelWidth, labelHeight);
        _label.TextAlign = ContentAlignment.MiddleCenter;
    }

    private static int GetCountdownBarReservedHeight()
    {
        return CountdownBarHeight + CountdownBarBottomMargin + CountdownBarAreaPadding;
    }

    private int GetHideStackIconReservePx()
    {
        var baseHideStackFontSize = Math.Max(10, _overlayFontSizePt - 1);
        var iconFontSize = Math.Max(10, (int)Math.Round(baseHideStackFontSize * 1.5));
        using var hideFont = new Font(OverlayFontFamily, iconFontSize, FontStyle.Bold);
        var iconTextSize = TextRenderer.MeasureText(
            HideStackIconGlyph,
            hideFont,
            new Size(int.MaxValue / 4, int.MaxValue / 4),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        return iconTextSize.Width + HideStackIconPaddingPx;
    }

    private void OnOverlayPaint(object? sender, PaintEventArgs e)
    {
        if (_showOverlayBorder || _showCopyTapFeedbackBorder)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(
                _showCopyTapFeedbackBorder ? _activeBorderColor : BorderColor,
                _showCopyTapFeedbackBorder ? CopyTapBorderWidth : 1.2f);
            var border = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundedRectanglePath(border, CornerRadius);
            e.Graphics.DrawPath(pen, path);
        }

        DrawListeningLevelMeter(e.Graphics);
        if (_lastUseFullWidthText)
            DrawFullWidthText(e.Graphics);

        if (!TryGetCountdownProgress(out var remainingFraction))
        {
            _countdownPlaybackIconBounds = Rectangle.Empty;
            return;
        }

        var trackMargin = Math.Max(8, Padding.Left);
        var iconText = _countdownPlaybackIcon;
        var iconTextSize = Size.Empty;
        if (!string.IsNullOrWhiteSpace(iconText))
        {
            using var iconFont = new Font(
                OverlayFontFamily,
                Math.Max(10, _overlayFontSizePt - 1),
                FontStyle.Regular);
            iconTextSize = TextRenderer.MeasureText(
                e.Graphics,
                iconText,
                iconFont,
                new Size(int.MaxValue / 4, int.MaxValue / 4),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        var iconSpacing = string.IsNullOrWhiteSpace(iconText) ? 0 : CountdownPlaybackIconGapPx;
        var trackWidth = Math.Max(
            80,
            Width - (trackMargin * 2) - iconTextSize.Width - iconSpacing);
        var trackTop = Math.Max(2, Height - CountdownBarBottomMargin - CountdownBarHeight);
        var trackBounds = new Rectangle(trackMargin, trackTop, trackWidth, CountdownBarHeight);
        var fillWidth = (int)Math.Round(trackBounds.Width * remainingFraction);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        if (fillWidth > 0)
        {
            var fillBounds = new Rectangle(trackBounds.Left, trackBounds.Top, fillWidth, trackBounds.Height);
            var fillColor = Color.FromArgb(220, _label.ForeColor);
            using var fillBrush = new SolidBrush(fillColor);
            using var fillPath = CreateRoundedRectanglePath(fillBounds, fillBounds.Height);
            e.Graphics.FillPath(fillBrush, fillPath);
        }

        if (string.IsNullOrWhiteSpace(iconText))
        {
            _countdownPlaybackIconBounds = Rectangle.Empty;
            return;
        }

        using var playbackIconFont = new Font(
            OverlayFontFamily,
            Math.Max(10, _overlayFontSizePt - 1),
            FontStyle.Regular);
        using var iconBrush = new SolidBrush(_label.ForeColor);
        var iconX = Math.Min(
            Math.Max(trackBounds.Right + iconSpacing, trackMargin),
            Math.Max(trackMargin, Width - Padding.Right - iconTextSize.Width));
        var iconY = Math.Max(
            0,
            trackBounds.Top - ((Math.Max(CountdownBarHeight, iconTextSize.Height) - CountdownBarHeight) / 2));
        var iconBounds = new Rectangle(
            iconX,
            iconY,
            Math.Max(1, iconTextSize.Width),
            Math.Max(1, Math.Min(Height - iconY - 2, Math.Max(CountdownBarHeight, iconTextSize.Height))));
        using var iconFormat = new StringFormat
        {
            LineAlignment = StringAlignment.Center,
            Alignment = StringAlignment.Near
        };
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.DrawString(iconText, playbackIconFont, iconBrush, iconBounds, iconFormat);
        _countdownPlaybackIconBounds = iconBounds;

        if (_showHideStackIcon)
            DrawHideStackIcon(e.Graphics);
    }

    private void DrawHideStackIcon(Graphics graphics)
    {
        var baseHideStackFontSize = Math.Max(10, _overlayFontSizePt - 1);
        var iconFontSize = Math.Max(10, (int)Math.Round(baseHideStackFontSize * 1.5));
        using var hideFont = new Font(OverlayFontFamily, iconFontSize, FontStyle.Bold);
        using var hideBrush = new SolidBrush(_label.ForeColor);
        using var hideFormat = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center
        };

        var iconTextSize = TextRenderer.MeasureText(
            HideStackIconGlyph,
            hideFont,
            new Size(int.MaxValue / 4, int.MaxValue / 4),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        var iconHeight = Math.Max(1, iconTextSize.Height);
        var iconY = Math.Max(
            _label.Bounds.Top,
            _label.Bounds.Top + Math.Max(0, (_label.Bounds.Height - iconHeight) / 2));
        var iconBounds = new Rectangle(
            Padding.Left,
            iconY,
            Math.Max(1, iconTextSize.Width),
            Math.Max(1, iconHeight));
        var clickableBounds = new Rectangle(
            iconBounds.Left,
            iconBounds.Top,
            Math.Max(1, iconTextSize.Width + HideStackIconPaddingPx),
            Math.Max(1, iconHeight));

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.DrawString(
            HideStackIconGlyph,
            hideFont,
            hideBrush,
            iconBounds,
            hideFormat);

        _hideStackIconBounds = clickableBounds;
    }

    private void DrawFullWidthText(Graphics graphics)
    {
        DrawFullWidthTextBlock(_label.Text, _label.Font, _label.Bounds, _label.ForeColor, graphics);
        if (_lastShowActionLine)
            DrawFullWidthTextBlock(_actionLabel.Text, _actionLabel.Font, _actionLabel.Bounds, _actionLabel.ForeColor, graphics);

        if (_lastShowPrefixLine)
            DrawFullWidthTextBlock(_prefixLabel.Text, _prefixLabel.Font, _prefixLabel.Bounds, _prefixLabel.ForeColor, graphics);
    }

    private void DrawFullWidthTextBlock(string? text, Font font, Rectangle bounds, Color color, Graphics graphics)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var lines = BuildFullWidthLines(graphics, text, font, Math.Max(1, bounds.Width));
        if (lines.Count == 0)
            return;

        var lineHeight = Math.Max(1, (int)Math.Ceiling(graphics.MeasureString("M", font).Height));
        var totalHeight = lineHeight * lines.Count;
        var startY = bounds.Top + Math.Max(0, (bounds.Height - totalHeight) / 2);
        var y = startY;

        foreach (var line in lines)
        {
            if (line.ShouldJustify)
            {
                DrawJustifiedLine(graphics, line.Text, font, bounds, color, lineHeight, y);
            }
            else
            {
                DrawStandardJustifiedLine(graphics, line.Text, font, bounds, color, y, lineHeight);
            }

            y += lineHeight;
        }
    }

    private void DrawStandardJustifiedLine(
        Graphics graphics,
        string text,
        Font font,
        Rectangle bounds,
        Color color,
        int y,
        int lineHeight)
    {
        if (string.IsNullOrEmpty(text))
            return;

        using var brush = new SolidBrush(color);
        var layout = new RectangleF(bounds.Left, y, bounds.Width, lineHeight);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            LineAlignment = StringAlignment.Near,
            Alignment = StringAlignment.Near
        };
        graphics.DrawString(text, font, brush, layout, format);
    }

    private void DrawJustifiedLine(
        Graphics graphics,
        string text,
        Font font,
        Rectangle bounds,
        Color color,
        int lineHeight,
        int y)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
        {
            DrawStandardJustifiedLine(graphics, text, font, bounds, color, y, lineHeight);
            return;
        }

        var spaceWidth = MeasureTextWidth(graphics, " ", font);
        var wordSizes = new int[words.Length];
        var used = 0;
        for (var i = 0; i < words.Length; i++)
        {
            var width = MeasureTextWidth(graphics, words[i], font);
            wordSizes[i] = width;
            used += width;
        }

        var gapCount = words.Length - 1;
        var spaceBudget = (bounds.Width - used - (spaceWidth * gapCount));
        if (spaceBudget <= 0)
        {
            DrawStandardJustifiedLine(graphics, text, font, bounds, color, y, lineHeight);
            return;
        }

        var baseExtra = spaceBudget / gapCount;
        var remainder = spaceBudget % gapCount;

        using var brush = new SolidBrush(color);
        var cursorX = bounds.Left;
        for (var i = 0; i < words.Length; i++)
        {
            graphics.DrawString(words[i], font, brush, cursorX, y);
            cursorX += wordSizes[i];
            if (i + 1 >= words.Length)
                continue;

            cursorX += spaceWidth + baseExtra + (i < remainder ? 1 : 0);
        }
    }

    private static int MeasureTextWidth(Graphics graphics, string text, Font font)
    {
        var size = TextRenderer.MeasureText(
            graphics,
            text,
            font,
            new Size(int.MaxValue / 4, int.MaxValue / 4),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return size.Width;
    }

    private static List<(string Text, bool ShouldJustify)> BuildFullWidthLines(
        Graphics graphics,
        string text,
        Font font,
        int maxWidthPx)
    {
        if (maxWidthPx <= 0)
            return new List<(string, bool)>();

        var result = new List<(string, bool)>();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        foreach (var paragraphLine in lines)
        {
            var normalizedParagraph = paragraphLine.Replace("\r", string.Empty);
            var words = normalizedParagraph.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                result.Add((string.Empty, false));
                continue;
            }

            var currentLine = string.Empty;
            foreach (var word in words)
            {
                var candidate = currentLine.Length == 0
                    ? word
                    : $"{currentLine} {word}";

                if (MeasureTextWidth(graphics, candidate, font) <= maxWidthPx)
                {
                    currentLine = candidate;
                    continue;
                }

                if (currentLine.Length > 0)
                {
                    result.Add((currentLine, true));
                    currentLine = word;
                    continue;
                }

                result.Add((word, false));
                currentLine = string.Empty;
            }

            if (currentLine.Length > 0)
                result.Add((currentLine, false));
        }

        return result;
    }

    private void DrawListeningLevelMeter(Graphics graphics)
    {
        if (!_showListeningLevelMeter)
            return;

        var labelArea = _label.Bounds.Width > 0 && _label.Bounds.Height > 0
            ? _label.Bounds
            : ClientRectangle;

        var lines = _label.Text.Split('\n');
        var firstLineHeight = Math.Max(
            _label.Font.Height,
            TextRenderer.MeasureText(
                lines.Length > 0 ? lines[0] : string.Empty,
                _label.Font,
                new Size(labelArea.Width, int.MaxValue),
                TextFormatFlags.NoPrefix).Height);
        var secondLineHeight = lines.Length > 1
            ? TextRenderer.MeasureText(
                lines[1],
                _label.Font,
                new Size(labelArea.Width, int.MaxValue),
                TextFormatFlags.NoPrefix).Height
            : 0;

        var candidateTop = labelArea.Top + (int)(labelArea.Height * 0.65) - (ListeningMeterHeight / 2);
        var minTop = Math.Max(Padding.Top, labelArea.Top + 2);
        var maxTop = Math.Max(
            minTop,
            labelArea.Bottom - Math.Max(16, secondLineHeight) - ListeningMeterHeight - 4);
        var meterTop = Math.Clamp(candidateTop + ListeningMeterTopOffsetPx, minTop, maxTop);

        var meterAreaWidth = Math.Min(ListeningMeterWidth, Math.Max(1, labelArea.Width));
        var meterLeft = Math.Clamp(
            labelArea.Left + ((labelArea.Width - meterAreaWidth) / 2),
            labelArea.Left,
            Math.Max(labelArea.Left, labelArea.Right - meterAreaWidth));
        var meterArea = new Rectangle(
            meterLeft,
            meterTop,
            meterAreaWidth,
            ListeningMeterHeight);

        var maxBarHeight = Math.Max(2, ListeningMeterHeight - 2);
        var barWidth = Math.Max(
            2,
            (ListeningMeterWidth - ((ListeningMeterBarCount - 1) * ListeningMeterBarSpacing))
            / ListeningMeterBarCount);
        var availableWidth = (barWidth * ListeningMeterBarCount) +
            (ListeningMeterBarSpacing * (ListeningMeterBarCount - 1));
        var widthOffset = Math.Max(0, (meterArea.Width - availableWidth) / 2);

        var nowMs = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerMillisecond;
        var inputLevel = Math.Clamp(_listeningLevelPercent, 0, 100) / 100.0;
        for (var i = 0; i < ListeningMeterBarCount; i++)
        {
            var phase = (nowMs / 90.0) + (i * 0.7);
            var wave = (Math.Sin(phase) + 1.0) / 2.0;
            var level = Math.Clamp(inputLevel * (0.65 + (0.35 * wave)), 0.08, 1.0);
            var barHeight = Math.Max(2, (int)Math.Round(maxBarHeight * level));
            var x = meterArea.Left + widthOffset + (i * (barWidth + ListeningMeterBarSpacing));
            var y = meterArea.Bottom - barHeight;

            var alpha = i == 0
                ? ListeningMeterActiveBarBaseAlpha
                : Math.Clamp(
                    ListeningMeterActiveBarBaseAlpha + (int)(55 * level),
                    ListeningMeterActiveBarBaseAlpha,
                    255);

            using var brush = new SolidBrush(
                inputLevel <= 0.02
                    ? ListeningMeterInactiveColor
                    : Color.FromArgb(alpha, ListeningMeterActiveColor));
            var barBounds = new Rectangle(x, y, barWidth, barHeight);
            graphics.FillRectangle(brush, barBounds);
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

        if (Opacity <= 0.02)
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
        var nextOpacity = Opacity - (1.0 / steps);
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

        if (_ignoreNextClickAfterDrag)
        {
            _ignoreNextClickAfterDrag = false;
            return;
        }

        if (HandleHideStackIconTap(sender, e))
            return;

        if (!_allowCopyTap)
            return;

        if (HandleCountdownPlaybackIconTap(sender, e))
            return;

        var textToCopy = GetOverlayTextForCopy(sender);
        if (!string.IsNullOrWhiteSpace(textToCopy))
        {
            SetCopyTapBorderVisible(true);
            try
            {
                Clipboard.SetText(textToCopy);
            }
            catch
            {
                // Ignore clipboard failures while preserving overlay interaction behavior.
            }

            OverlayCopyTapped?.Invoke(this, new OverlayCopyTappedEventArgs(_activeMessageId, textToCopy));
        }

        if (!_tapToCancelEnabled)
            return;

        if (_tapToCancelMessageId == 0 || _tapToCancelMessageId != _activeMessageId)
            return;

        var messageId = _tapToCancelMessageId;
        ResetTapToCancel();
        OverlayTapped?.Invoke(this, new OverlayTappedEventArgs(messageId));
    }

    private bool HandleCountdownPlaybackIconTap(object? sender, MouseEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_countdownPlaybackIcon))
            return false;

        var clickPoint = TranslateToFormClientPoint(sender, e.Location);
        if (_countdownPlaybackIconBounds.IsEmpty || !_countdownPlaybackIconBounds.Contains(clickPoint))
            return false;

        _tapHandledForCurrentPress = true;
        OverlayCountdownPlaybackIconTapped?.Invoke(
            this,
            new OverlayCountdownPlaybackIconTappedEventArgs(_activeMessageId, _countdownPlaybackIcon));
        return true;
    }

    private bool HandleHideStackIconTap(object? sender, MouseEventArgs e)
    {
        if (!_showHideStackIcon)
            return false;

        var clickPoint = TranslateToFormClientPoint(sender, e.Location);
        if (_hideStackIconBounds.IsEmpty || !_hideStackIconBounds.Contains(clickPoint))
            return false;

        _tapHandledForCurrentPress = true;
        OverlayHideStackIconTapped?.Invoke(this, new OverlayHideStackIconTappedEventArgs(_activeMessageId));
        return true;
    }

    private static Point TranslateToFormClientPoint(object? sender, Point point)
    {
        return sender is Control control
            ? control.PointToClient(control.PointToScreen(point))
            : point;
    }

    public void SetCopyTapBorderVisible(bool visible)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetCopyTapBorderVisible(visible)));
            return;
        }

        _activeBorderColor = visible
            ? Color.FromArgb(
                CopyTapBorderAlpha,
                _label.ForeColor.R,
                _label.ForeColor.G,
                _label.ForeColor.B)
            : BorderColor;
        _showCopyTapFeedbackBorder = visible;
        Invalidate();
    }

    private bool HandleOverlayTap(object? sender, MouseEventArgs e)
    {
        OnOverlayMouseClick(sender, e);
        return true;
    }

    private string GetOverlayTextForCopy(object? sender)
    {
        if (!string.IsNullOrWhiteSpace(_copyText))
            return _copyText;

        return sender switch
        {
            null => _label.Text,
            Label => _label.Text,
            _ => _label.Text
        };
    }

    private void OnOverlayMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        if (!_pressInProgress)
        {
            _pressInProgress = true;
            _tapHandledForCurrentPress = false;
        }

        _dragStarted = true;
        _isHorizontalDragging = false;
        _ignoreNextClickAfterDrag = false;
        _lastDragScreenX = Cursor.Position.X;
        Capture = true;
    }

    private void OnOverlayMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragStarted)
            return;

        var screenX = Cursor.Position.X;
        var deltaX = screenX - _lastDragScreenX;
        if (!_isHorizontalDragging)
        {
            if (Math.Abs(deltaX) < HorizontalDragActivationThreshold)
            {
                _lastDragScreenX = screenX;
                return;
            }

            _isHorizontalDragging = true;
            Cursor = Cursors.SizeWE;
        }

        _lastDragScreenX = screenX;
        OverlayHorizontalDragged?.Invoke(this, new OverlayHorizontalDraggedEventArgs(deltaX));
    }

    private void OnOverlayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var wasDrag = EndHorizontalDrag();
        if (!_pressInProgress)
            return;

        if (!wasDrag && !_tapHandledForCurrentPress)
        {
            HandleOverlayTap(sender, e);
            _tapHandledForCurrentPress = true;
        }

        _pressInProgress = false;
        _tapHandledForCurrentPress = false;
        if (wasDrag)
            return;
    }

    private void OnMouseCaptureChanged(object? sender, EventArgs e)
    {
        if (!Capture)
            EndHorizontalDrag();
    }

    private bool EndHorizontalDrag()
    {
        var wasDragging = _isHorizontalDragging;
        if (!_dragStarted && !_isHorizontalDragging)
        {
            Capture = false;
            Cursor = Cursors.Default;
            return false;
        }

        _ignoreNextClickAfterDrag = _isHorizontalDragging;
        _dragStarted = false;
        _isHorizontalDragging = false;
        Cursor = Cursors.Default;
        Capture = false;
        return wasDragging;
    }

    private void RegisterDragHandlers(Control control)
    {
        control.MouseDown += OnOverlayMouseDown;
        control.MouseMove += OnOverlayMouseMove;
        control.MouseUp += OnOverlayMouseUp;

        foreach (Control child in control.Controls)
            RegisterDragHandlers(child);
    }

    private static Color EnsureOpaque(Color color)
    {
        return Color.FromArgb(255, color.R, color.G, color.B);
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

public sealed class OverlayCopyTappedEventArgs : EventArgs
{
    public OverlayCopyTappedEventArgs(int messageId, string copiedText)
    {
        MessageId = messageId;
        CopiedText = copiedText;
    }

    public int MessageId { get; }
    public string CopiedText { get; }
}

public sealed class OverlayHorizontalDraggedEventArgs : EventArgs
{
    public OverlayHorizontalDraggedEventArgs(int deltaX)
    {
        DeltaX = deltaX;
    }

    public int DeltaX { get; }
}

public sealed class OverlayCountdownPlaybackIconTappedEventArgs : EventArgs
{
    public OverlayCountdownPlaybackIconTappedEventArgs(int messageId, string? iconText)
    {
        MessageId = messageId;
        IconText = iconText;
    }

    public int MessageId { get; }
    public string? IconText { get; }
}

public sealed class OverlayHideStackIconTappedEventArgs : EventArgs
{
    public OverlayHideStackIconTappedEventArgs(int messageId)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }
}
