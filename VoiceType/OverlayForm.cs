using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VoiceType;

public class OverlayForm : Form
{
    private static readonly Color DefaultTextColor = Color.FromArgb(255, 255, 188);
    private const string OverlayFontFamilyFallback = "Segoe UI";
    private const string OverlayBundledFontFileName = "VoiceTypeOverlay-Regular.ttf";
    private const string SymbolsNerdFontFileName = "SymbolsNerdFontMono-Regular.ttf";
    private const string SymbolsNerdFontFamilyFallback = "Symbols Nerd Font Mono";
    private const string OverlayBundledFontRelativePath = "Assets\\Fonts";
    private static readonly PrivateFontCollection OverlayFontCollection = new();
    private static readonly string OverlayFontFamily = ResolveOverlayFontFamily();
    private static readonly string SymbolsNerdFontFamily = ResolveSymbolsNerdFontFamily();
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
    private static readonly Color HoverOverlayBackgroundColor = Color.FromArgb(255, 34, 40, 55);
    private const float CopyTapBorderWidth = 3.0f;
    private const int CopyTapBorderAlpha = 255;
    private const int CountdownPlaybackIconGapPx = 10;
    private const int HideStackIconVerticalOffsetPx = 0;
    private const int HideStackIconPaddingPx = 18;
    private const int HideStackIconHorizontalPaddingPx = 2;
    private const int HideStackIconHorizontalOffsetPx = 2;
    private const int HelloHideStackIconHorizontalOffsetPx = -12;
    private const int HideStackIconCornerRadius = 6;
    private const int HideStackIconMinHeight = 30;
    private const int HideStackIconMinWidth = 28;
    private const int HideStackIconMinInset = 2;
    private const float ListeningOverlayIconScale = 0.5f;
    private static readonly Color HideStackIconFillColor = Color.FromArgb(255, 208, 52, 52);
    private static readonly Color HideStackIconStrokeColor = Color.FromArgb(255, 255, 214, 214);
    private static readonly Color HideStackIconGlyphColor = Color.FromArgb(255, 255, 236, 236);
    private const int StartListeningIconMinHeight = 30;
    private const int StartListeningIconMinWidth = 28;
    private const int StartListeningIconPaddingPx = 18;
    private const int StartListeningIconHorizontalOffsetPx = 2;
    private const int StartListeningIconHorizontalPaddingPx = 2;
    private const int StartListeningIconMinInset = 2;
    private const int StartListeningIconVerticalOffsetPx = 0;
    private static readonly Color StartListeningIconFillColor = Color.FromArgb(255, 34, 171, 77);
    private static readonly Color StartListeningIconStrokeColor = Color.FromArgb(255, 206, 255, 214);
    private static readonly Color StartListeningIconGlyphColor = Color.FromArgb(255, 243, 255, 245);
    private const int StartListeningIconCornerRadius = 8;
    private const int StopListeningIconMinHeight = 30;
    private const int StopListeningIconMinWidth = 28;
    private const int StopListeningIconPaddingPx = 18;
    private const int StopListeningIconHorizontalOffsetPx = 2;
    private const int StopListeningIconHorizontalPaddingPx = 2;
    private const int StopListeningIconMinInset = 2;
    private const int StopListeningIconVerticalOffsetPx = 0;
    private const int StopListeningIconCornerRadius = 8;
    private static readonly Color StopListeningIconFillColor = Color.FromArgb(255, 229, 57, 53);
    private static readonly Color StopListeningIconStrokeColor = Color.FromArgb(255, 255, 214, 214);
    private static readonly Color StopListeningIconGlyphColor = Color.FromArgb(255, 255, 236, 236);
    private const int CancelListeningIconMinHeight = 30;
    private const int CancelListeningIconMinWidth = 28;
    private const int CancelListeningIconPaddingPx = 18;
    private const int CancelListeningIconHorizontalOffsetPx = 2;
    private const int CancelListeningIconHorizontalPaddingPx = 2;
    private const int CancelListeningIconMinInset = 2;
    private const int CancelListeningIconVerticalOffsetPx = 0;
    private static readonly Color CancelListeningIconFillColor = Color.FromArgb(255, 255, 112, 67);
    private static readonly Color CancelListeningIconStrokeColor = Color.FromArgb(255, 255, 214, 214);
    private static readonly Color CancelListeningIconGlyphColor = Color.FromArgb(255, 255, 236, 236);
    private const float HelloTextFrameWidth = 1.0f;
    private const int HelloTextFramePaddingPx = 2;
    private static readonly Color HelloTextFrameColor = Color.FromArgb(255, 240, 245, 255);
    private static readonly Dictionary<string, string> NerdFontIconClassToGlyph = new(StringComparer.OrdinalIgnoreCase)
    {
        { "nf-md-close_box", "\U0000F0157" },
        { "md-close_box", "\U0000F0157" },
        { "nf-md-record_rec", "\U0000F044B" },
        { "md-record_rec", "\U0000F044B" },
        { "nf-fa-stop", "\U0000F04D" },
        { "fa-stop", "\U0000F04D" }
    };

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
    private bool? _showOverlayBorderOverride;
    private int _overlayBackgroundMode = AppConfig.DefaultOverlayBackgroundMode;
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
    private int _fullWidthRenderFailureMessageId;
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
    private bool _showStartListeningIcon;
    private bool _showStopListeningIcon;
    private bool _showCancelListeningIcon;
    private bool _showHelloTextFrame;
    private string? _hideStackIconGlyph;
    private string? _startListeningIconGlyph;
    private string? _stopListeningIconGlyph;
    private string? _cancelListeningIconGlyph;
    private Rectangle _hideStackIconBounds = Rectangle.Empty;
    private Rectangle _startListeningIconBounds = Rectangle.Empty;
    private Rectangle _stopListeningIconBounds = Rectangle.Empty;
    private Rectangle _cancelListeningIconBounds = Rectangle.Empty;
    private float _overlayIconScale = 1.0f;
    private bool _isMouseOverOverlay;
    private int _mouseOverlayDepth;
    private static int _profiledOverlayControlIconHeightPx = HideStackIconMinHeight;
    private int _lastLoggedHideStackMessageId;
    private DateTime _nextHideStackPositionLogAt = DateTime.MinValue;
    private static readonly HashSet<string> LoggedNerdFontIconResolution = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedNerdFontIconRenderFailure = new(StringComparer.Ordinal);

    public event EventHandler<OverlayTappedEventArgs>? OverlayTapped;
    public event EventHandler<OverlayCopyTappedEventArgs>? OverlayCopyTapped;
    public event EventHandler<OverlayHorizontalDraggedEventArgs>? OverlayHorizontalDragged;
    public event EventHandler<OverlayCountdownPlaybackIconTappedEventArgs>? OverlayCountdownPlaybackIconTapped;
    public event EventHandler<OverlayHideStackIconTappedEventArgs>? OverlayHideStackIconTapped;
    public event EventHandler<OverlayStartListeningIconTappedEventArgs>? OverlayStartListeningIconTapped;
    public event EventHandler<OverlayStopListeningIconTappedEventArgs>? OverlayStopListeningIconTapped;
    public event EventHandler<OverlayCancelListeningIconTappedEventArgs>? OverlayCancelListeningIconTapped;

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
            Font = CreateOverlayFont(AppConfig.DefaultOverlayFontSizePt + 1, FontStyle.Bold),
            ForeColor = DefaultTextColor,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = false,
            UseCompatibleTextRendering = false
        };
        _actionLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Font = CreateOverlayFont(Math.Max(10, AppConfig.DefaultOverlayFontSizePt - 1), FontStyle.Bold),
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
            Font = CreateOverlayFont(Math.Max(10, AppConfig.DefaultOverlayFontSizePt - 2), FontStyle.Bold),
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
        RegisterHoverHandlers(this);
        MouseCaptureChanged += OnMouseCaptureChanged;

        Paint += OnOverlayPaint;
        ApplyHudSettings(
            AppConfig.DefaultOverlayOpacityPercent,
            AppConfig.DefaultOverlayWidthPercent,
            AppConfig.DefaultOverlayFontSizePt,
            showBorder: true,
            AppConfig.DefaultOverlayBackgroundMode);
        UpdateRoundedRegion();

        // Position: bottom-center, above taskbar
        PositionOnScreen();
    }

    private static string ResolveOverlayFontFamily()
    {
        return ResolveBundledFontFamily(
            OverlayBundledFontFileName,
            OverlayFontFamilyFallback,
            "overlay");
    }

    private static string ResolveSymbolsNerdFontFamily()
    {
        return ResolveBundledFontFamily(
            SymbolsNerdFontFileName,
            SymbolsNerdFontFamilyFallback,
            "symbols");
    }

    private static string ResolveBundledFontFamily(string fileName, string fallbackFontFamily, string fontGroup)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, OverlayBundledFontRelativePath, fileName);
        if (!File.Exists(fontPath))
        {
            Log.Error($"Bundled {fontGroup} font missing from output folder: '{fontPath}'.");
            Log.Error(
                $"Expected font folder exists: {Directory.Exists(Path.Combine(AppContext.BaseDirectory, OverlayBundledFontRelativePath))} " +
                $"(AppContext.BaseDirectory={AppContext.BaseDirectory}).");

            if (IsFontFamilyAvailable(fallbackFontFamily))
            {
                Log.Info($"Using fallback font '{fallbackFontFamily}' for {fontGroup} overlays because bundled font is missing.");
                return fallbackFontFamily;
            }

            Log.Error($"Bundled {fontGroup} fallback unavailable: {fallbackFontFamily}.");
            return FontFamily.GenericSansSerif.Name;
        }

        try
        {
            var previousFamilyCount = OverlayFontCollection.Families.Length;
            OverlayFontCollection.AddFontFile(fontPath);
            var families = OverlayFontCollection.Families;
            if (families.Length > previousFamilyCount)
            {
                var familyName = families[families.Length - 1].Name;
                Log.Info($"Using bundled {fontGroup} font: {familyName} ({fontPath})");
                return familyName;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to load bundled {fontGroup} font '{fileName}'. Message={ex.Message}");
        }

        if (IsFontFamilyAvailable(fallbackFontFamily))
            return fallbackFontFamily;

        Log.Error($"Bundled {fontGroup} font fallback unavailable: {fallbackFontFamily}.");

        return FontFamily.GenericSansSerif.Name;
    }

    private static bool IsFontFamilyAvailable(string familyName)
    {
        try
        {
            using var font = new Font(familyName, Math.Max(10, AppConfig.DefaultOverlayFontSizePt - 1));
            return string.Equals(font.FontFamily.Name, familyName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException ex)
        {
            Log.Error($"Font family check failed for '{familyName}'. Message={ex.Message}");
            return false;
        }
    }

    private static Font CreateOverlayFont(float sizePt, FontStyle style = FontStyle.Regular)
    {
        var fontSize = Math.Max(10, sizePt);
        try
        {
            return new Font(OverlayFontFamily, fontSize, style);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create overlay font '{OverlayFontFamily}' size {fontSize} style {style}. Message={ex.Message}");
            return new Font(FontFamily.GenericSansSerif, fontSize, style);
        }
    }

    private static Font CreateNerdFont(float sizePt, FontStyle style = FontStyle.Regular)
    {
        var fontSize = Math.Max(10, sizePt);
        try
        {
            return new Font(SymbolsNerdFontFamily, fontSize, style);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to create Nerd icon font '{SymbolsNerdFontFamily}' size {fontSize} style {style}. Message={ex.Message}");
            return CreateOverlayFont(fontSize, style);
        }
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
        e.Graphics.Clear(GetOverlayBackgroundColor());
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
            ClearMouseOverState();
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
            _showStartListeningIcon = false;
            _showStopListeningIcon = false;
            _showCancelListeningIcon = false;
            _showHelloTextFrame = false;
            _showOverlayBorderOverride = null;
            _hideStackIconGlyph = null;
            _startListeningIconGlyph = null;
            _stopListeningIconGlyph = null;
            _cancelListeningIconGlyph = null;
            _overlayIconScale = 1.0f;
            _hideStackIconBounds = Rectangle.Empty;
            _startListeningIconBounds = Rectangle.Empty;
            _stopListeningIconBounds = Rectangle.Empty;
            _cancelListeningIconBounds = Rectangle.Empty;
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
        bool showHideStackIcon = false,
        bool showStartListeningIcon = false,
        bool showStopListeningIcon = false,
        bool showCancelListeningIcon = false,
        bool showHelloTextFrame = false,
        bool? showOverlayBorder = null,
        string? hideStackIconGlyph = null,
        string? startListeningIconGlyph = null,
        string? stopListeningIconGlyph = null,
        string? cancelListeningIconGlyph = null)
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
                showHideStackIcon,
                showStartListeningIcon,
                showStopListeningIcon,
                showCancelListeningIcon,
                showHelloTextFrame,
                showOverlayBorder,
                hideStackIconGlyph,
                startListeningIconGlyph,
                stopListeningIconGlyph,
                cancelListeningIconGlyph)));
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
            _showStartListeningIcon = showStartListeningIcon;
            _showStopListeningIcon = showStopListeningIcon;
            _showCancelListeningIcon = showCancelListeningIcon;
            _showOverlayBorderOverride = showOverlayBorder;
            _showHelloTextFrame = showHelloTextFrame;
            _hideStackIconGlyph = ResolveNerdFontIconGlyph(hideStackIconGlyph);
            _startListeningIconGlyph = ResolveNerdFontIconGlyph(startListeningIconGlyph);
            _stopListeningIconGlyph = ResolveNerdFontIconGlyph(stopListeningIconGlyph);
            _cancelListeningIconGlyph = ResolveNerdFontIconGlyph(cancelListeningIconGlyph);
            _overlayIconScale = (showListeningLevelMeter
                || showHideStackIcon
                || showStartListeningIcon
                || showStopListeningIcon
                || showCancelListeningIcon)
                ? ListeningOverlayIconScale
                : 1.0f;
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
            _fullWidthRenderFailureMessageId = -1;
            _hideStackIconBounds = Rectangle.Empty;
            _startListeningIconBounds = Rectangle.Empty;
            _stopListeningIconBounds = Rectangle.Empty;
            _cancelListeningIconBounds = Rectangle.Empty;
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

    public void ApplyHudSettings(
        int opacityPercent,
        int widthPercent,
        int fontSizePt,
        bool showBorder,
        int overlayBackgroundMode)
    {
        if (InvokeRequired)
        {
            Invoke(() => ApplyHudSettings(
                opacityPercent,
                widthPercent,
                fontSizePt,
                showBorder,
                overlayBackgroundMode));
            return;
        }

        _overlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent(widthPercent);
        _overlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt(Math.Min(AppConfig.MaxOverlayFontSizePt, fontSizePt + 3));
        _showOverlayBorder = showBorder;
        _overlayBackgroundMode = AppConfig.NormalizeOverlayBackgroundMode(overlayBackgroundMode);
        _baseOpacity = AppConfig.NormalizeOverlayOpacityPercent(opacityPercent) / 100.0;
        Opacity = 1.0;

        var oldFont = _label.Font;
        _label.Font = CreateOverlayFont(_overlayFontSizePt, FontStyle.Bold);
        oldFont.Dispose();
        var oldActionFont = _actionLabel.Font;
        _actionLabel.Font = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 2), FontStyle.Bold);
        oldActionFont.Dispose();
        var oldPrefixFont = _prefixLabel.Font;
        _prefixLabel.Font = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 2), FontStyle.Bold);
        oldPrefixFont.Dispose();

        ApplyBackgroundVisuals();
    }

    private bool ShouldShowContrastBackground()
    {
        return _overlayBackgroundMode switch
        {
            AppConfig.OverlayBackgroundModeAlways => true,
            AppConfig.OverlayBackgroundModeHoverOnly => _isMouseOverOverlay,
            _ => false
        };
    }

    private void ApplyBackgroundVisuals()
    {
        var shouldShowContrastBackground = ShouldShowContrastBackground();
        BackColor = shouldShowContrastBackground
            ? HoverOverlayBackgroundColor
            : TransparentOverlayBackgroundColor;
        TransparencyKey = shouldShowContrastBackground
            ? Color.Empty
            : TransparentOverlayBackgroundColor;
        Opacity = shouldShowContrastBackground
            ? 1.0
            : _baseOpacity;
        Invalidate();
    }

    private Color GetOverlayBackgroundColor()
    {
        return ShouldShowContrastBackground()
            ? HoverOverlayBackgroundColor
            : TransparentOverlayBackgroundColor;
    }

    private bool ShouldShowOverlayBorder()
    {
        return _showOverlayBorderOverride ?? _showOverlayBorder;
    }

    private static string? ResolveNerdFontIconGlyph(string? iconClass)
    {
        if (string.IsNullOrWhiteSpace(iconClass))
            return null;

        var normalized = iconClass.Trim();
        if (normalized.Length == 2 && char.IsSurrogatePair(normalized, 0))
            return normalized;
        if (normalized.Length == 1)
            return normalized;

        if (normalized.StartsWith(@"\u", StringComparison.OrdinalIgnoreCase) && normalized.Length == 6)
        {
            normalized = normalized.AsSpan(2).ToString();
        }
        else if (normalized.StartsWith(@"\U", StringComparison.OrdinalIgnoreCase) && normalized.Length == 10)
        {
            normalized = normalized.AsSpan(2).ToString();
        }

        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.AsSpan(2).ToString();

        if (normalized.Length is >= 4 and <= 6)
        {
            var looksHex = true;
            for (var i = 0; i < normalized.Length; i++)
            {
                if (!IsHexDigit(normalized[i]))
                {
                    looksHex = false;
                    break;
                }
            }

            if (looksHex && int.TryParse(normalized, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
                return char.ConvertFromUtf32(codePoint);
        }

        return NerdFontIconClassToGlyph.TryGetValue(normalized, out var glyph)
            ? glyph
            : null;
    }

    private static bool IsHexDigit(char c)
    {
        return (c >= '0' && c <= '9') ||
               (c >= 'A' && c <= 'F') ||
               (c >= 'a' && c <= 'f');
    }

    private static string DescribeNerdGlyph(string? iconGlyph)
    {
        if (string.IsNullOrEmpty(iconGlyph))
            return "<empty>";

        try
        {
            var codePoint = char.ConvertToUtf32(iconGlyph, 0);
            return $"U+{codePoint:X4}";
        }
        catch
        {
            return $"<invalid:{iconGlyph}>";
        }
    }

    private bool DrawNerdFontIcon(
        Graphics graphics,
        Rectangle iconBounds,
        Color iconColor,
        string? iconGlyph)
    {
        var originalIconValue = string.IsNullOrWhiteSpace(iconGlyph) ? "<null>" : iconGlyph.Trim();
        var resolvedIcon = ResolveNerdFontIconGlyph(iconGlyph);
        if (string.IsNullOrWhiteSpace(resolvedIcon))
        {
            if (LoggedNerdFontIconResolution.Add($"missing:{originalIconValue}"))
                Log.Info($"Nerd icon not resolved, overlay fallback applied. icon={originalIconValue}.");
            return false;
        }

        if (LoggedNerdFontIconResolution.Add($"resolved:{originalIconValue}:{DescribeNerdGlyph(resolvedIcon)}"))
            Log.Info($"Nerd icon resolved. icon={originalIconValue}, glyph={DescribeNerdGlyph(resolvedIcon)}.");

        var iconSizePx = Math.Max(12, Math.Min(iconBounds.Width, iconBounds.Height) - 3);
        var fontSizePx = Math.Max(10.0f, Math.Min(48.0f, iconSizePx * 0.9f));
        using var iconFont = CreateNerdFont(fontSizePx, FontStyle.Regular);
        try
        {
            var textSize = TextRenderer.MeasureText(
                graphics,
                resolvedIcon,
                iconFont,
                new Size(int.MaxValue / 4, int.MaxValue / 4),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var glyphBounds = new Rectangle(
                iconBounds.Left + Math.Max(0, (iconBounds.Width - textSize.Width) / 2),
                iconBounds.Top + Math.Max(0, (iconBounds.Height - textSize.Height) / 2),
                Math.Max(1, Math.Min(textSize.Width, iconBounds.Width)),
                Math.Max(1, Math.Min(textSize.Height, iconBounds.Height)));

            TextRenderer.DrawText(
                graphics,
                resolvedIcon,
                iconFont,
                glyphBounds,
                iconColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        catch (Exception ex)
        {
            if (LoggedNerdFontIconRenderFailure.Add($"{originalIconValue}:{DescribeNerdGlyph(resolvedIcon)}"))
                Log.Error($"Failed Nerd icon render path. icon={originalIconValue}, glyph={DescribeNerdGlyph(resolvedIcon)}, bounds={iconBounds}, color={iconColor}, font={iconFont.Name}, Message={ex.Message}");

            return false;
        }

        return true;
    }

    private void ApplyMouseOverVisuals(bool isMouseOver)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyMouseOverVisuals(isMouseOver)));
            return;
        }

        _isMouseOverOverlay = isMouseOver;
        ApplyBackgroundVisuals();
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
        if (InvokeRequired)
        {
            Invoke((Action)(() => FadeOut(delayMilliseconds, fadeDurationMs, fadeTickIntervalMs)));
            return;
        }

        if (delayMilliseconds > 0 && Visible)
        {
            System.Windows.Forms.Timer? timer = null;
            timer = new System.Windows.Forms.Timer { Interval = delayMilliseconds };
            timer.Tick += (_, _) =>
            {
                timer?.Stop();
                timer?.Dispose();
                FadeOutInternal(fadeDurationMs, fadeTickIntervalMs);
            };
            timer.Start();
            return;
        }

        FadeOutInternal(fadeDurationMs, fadeTickIntervalMs);
    }

    private void FadeOutInternal(int? fadeDurationMs, int? fadeTickIntervalMs)
    {
        _fadeDurationMs = fadeDurationMs is null or <= 0
            ? DefaultFadeDurationMs
            : Math.Max(60, fadeDurationMs.Value);
        _fadeTickIntervalMs = fadeTickIntervalMs is null or <= 0
            ? DefaultFadeTickIntervalMs
            : Math.Clamp(fadeTickIntervalMs.Value, 16, 500);
        _fadeTimer.Interval = _fadeTickIntervalMs;
        _fadeMessageId = unchecked(_activeMessageId);
        _fadeMessageId = Math.Max(_fadeMessageId, 1);
        _fadeTimer.Start();
    }

    private bool IsMouseOverOverlay()
    {
        return _isMouseOverOverlay;
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

    private void RegisterHoverHandlers(Control control)
    {
        control.MouseEnter += OnOverlayMouseEnter;
        control.MouseLeave += OnOverlayMouseLeave;

        foreach (Control child in control.Controls)
            RegisterHoverHandlers(child);
    }

    private void OnOverlayMouseEnter(object? sender, EventArgs e)
    {
        if (!Visible)
            return;

        if (_mouseOverlayDepth == 0)
            ApplyMouseOverVisuals(true);

        _mouseOverlayDepth++;
    }

    private void OnOverlayMouseLeave(object? sender, EventArgs e)
    {
        if (!Visible)
        {
            ClearMouseOverState();
            return;
        }

        _mouseOverlayDepth = Math.Max(0, _mouseOverlayDepth - 1);
        if (_mouseOverlayDepth == 0)
            ApplyMouseOverVisuals(false);
    }

    private void ClearMouseOverState()
    {
        _mouseOverlayDepth = 0;
        ApplyMouseOverVisuals(false);
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
        var startListeningIconReservePx = _showStartListeningIcon
            ? GetStartListeningIconReservePx()
            : 0;
        var stopListeningIconReservePx = _showStopListeningIcon
            ? GetStopListeningIconReservePx()
            : 0;
        var cancelListeningIconReservePx = _showCancelListeningIcon
            ? GetCancelListeningIconReservePx()
            : 0;
        var contentLeft = Padding.Left + hideStackIconReservePx;
        var contentWidth = Math.Max(
            1,
            ClientSize.Width - Padding.Horizontal - hideStackIconReservePx - startListeningIconReservePx - stopListeningIconReservePx - cancelListeningIconReservePx);

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

    private int GetOverlayControlIconReferenceHeight()
    {
        var iconReferenceBounds = _label.Bounds;
        if (iconReferenceBounds.IsEmpty || iconReferenceBounds.Height <= 0)
            iconReferenceBounds = GetHideStackIconReferenceBounds();

        var iconHeight = Math.Max(1, iconReferenceBounds.Height);
        if (_showHelloTextFrame)
        {
            iconHeight += (HelloTextFramePaddingPx * 2);
            _profiledOverlayControlIconHeightPx = Math.Max(_profiledOverlayControlIconHeightPx, iconHeight);
        }

        return Math.Max(iconHeight, _profiledOverlayControlIconHeightPx);
    }

    private float GetOverlayIconScale()
    {
        return _overlayIconScale;
    }

    private static Rectangle ScaleBoundsAroundCenter(Rectangle bounds, float scale)
    {
        if (bounds.IsEmpty || scale >= 0.999f)
            return bounds;

        var centerX = bounds.Left + (bounds.Width / 2.0f);
        var centerY = bounds.Top + (bounds.Height / 2.0f);
        var scaledWidth = Math.Max(1, (int)Math.Round(bounds.Width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Round(bounds.Height * scale));

        return new Rectangle(
            (int)Math.Round(centerX - (scaledWidth / 2.0f)),
            (int)Math.Round(centerY - (scaledHeight / 2.0f)),
            scaledWidth,
            scaledHeight);
    }

    private int GetHideStackIconReservePx()
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconRenderWidth = Math.Max(
            HideStackIconMinWidth,
            iconHeight + HideStackIconHorizontalPaddingPx + HideStackIconPaddingPx);

        return iconRenderWidth + Math.Max(0, HideStackIconHorizontalOffsetPx);
    }

    private int GetStartListeningIconReservePx()
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconRenderWidth = Math.Max(
            StartListeningIconMinWidth,
            iconHeight + StartListeningIconHorizontalPaddingPx + HideStackIconPaddingPx);

        return iconRenderWidth + Math.Max(0, StartListeningIconHorizontalOffsetPx);
    }

    private int GetStopListeningIconReservePx()
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconRenderWidth = Math.Max(
            StopListeningIconMinWidth,
            iconHeight + StopListeningIconHorizontalPaddingPx + HideStackIconPaddingPx);

        return iconRenderWidth + Math.Max(0, StopListeningIconHorizontalOffsetPx);
    }

    private int GetCancelListeningIconReservePx()
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconRenderWidth = Math.Max(
            CancelListeningIconMinWidth,
            iconHeight + CancelListeningIconHorizontalPaddingPx + HideStackIconPaddingPx);

        return iconRenderWidth + Math.Max(0, CancelListeningIconHorizontalOffsetPx);
    }

    private void OnOverlayPaint(object? sender, PaintEventArgs e)
    {
        try
        {
            if (ShouldShowOverlayBorder())
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(BorderColor, 1.2f);
                var border = new Rectangle(0, 0, Width - 1, Height - 1);
                using var path = CreateRoundedRectanglePath(border, CornerRadius);
                e.Graphics.DrawPath(pen, path);
            }

        DrawListeningLevelMeter(e.Graphics);
        if (_lastUseFullWidthText)
        {
            if (_fullWidthRenderFailureMessageId == _activeMessageId)
            {
                DrawFallbackFullWidthText(e.Graphics);
            }
            else
            {
                DrawFullWidthText(e.Graphics);
            }
        }


        var hasCountdown = TryGetCountdownProgress(out var remainingFraction);
        if (hasCountdown)
        {
            var trackMargin = Math.Max(8, Padding.Left);
            var iconText = _countdownPlaybackIcon;
            var iconTextSize = Size.Empty;
            if (!string.IsNullOrWhiteSpace(iconText))
            {
                using var iconFont = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 1), FontStyle.Regular);
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

            if (!string.IsNullOrWhiteSpace(iconText))
            {
                using var playbackIconFont = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 1), FontStyle.Regular);
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
            }
            else
            {
                _countdownPlaybackIconBounds = Rectangle.Empty;
            }
        }
        else
        {
            _countdownPlaybackIconBounds = Rectangle.Empty;
        }

        if (_showHideStackIcon)
            DrawHideStackIcon(e.Graphics);

        var rightReservedPx = 0;
        if (_showStopListeningIcon)
        {
            DrawStopListeningIcon(e.Graphics, rightReservedPx);
            rightReservedPx += GetStopListeningIconReservePx();
        }

        if (_showCancelListeningIcon)
        {
            DrawCancelListeningIcon(e.Graphics, rightReservedPx);
            rightReservedPx += GetCancelListeningIconReservePx();
        }

        if (_showStartListeningIcon)
            DrawStartListeningIcon(e.Graphics, rightReservedPx);
        }
        catch (Exception ex)
        {
            if (_lastUseFullWidthText)
            {
                if (_fullWidthRenderFailureMessageId != _activeMessageId)
                {
                    _fullWidthRenderFailureMessageId = _activeMessageId;
                    Log.Error(
                        $"Overlay paint failed and switched to fallback rendering. " +
                        $"Type={ex.GetType().Name}, Message={ex.Message}, OverlaySize={Width}x{Height}, " +
                        $"MessageId={_activeMessageId}, FullWidth={_lastUseFullWidthText}, Opacity={Opacity:F2}, " +
                        $"ShowBorder={ShouldShowOverlayBorder()}, ShowHelloTextFrame={_showHelloTextFrame}");
                    if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    {
                        Log.Error($"Overlay paint stack trace: {ex.StackTrace}");
                    }
                }

                try
                {
                    e.Graphics.Clear(TransparentOverlayBackgroundColor);
                    DrawFallbackFullWidthText(e.Graphics);
                    DrawOverlayWarningIcons(e.Graphics);
                }
                catch (Exception fallbackEx)
                {
                    Log.Error($"Full-width overlay fallback rendering failed: {fallbackEx}");
                }
                return;
            }

            try
            {
                Log.Error(
                    $"Overlay paint failed and switched to fallback rendering. " +
                    $"Type={ex.GetType().Name}, Message={ex.Message}, OverlaySize={Width}x{Height}, " +
                    $"MessageId={_activeMessageId}, FullWidth={_lastUseFullWidthText}, Opacity={Opacity:F2}, " +
                    $"ShowBorder={ShouldShowOverlayBorder()}, ShowHelloTextFrame={_showHelloTextFrame}");
                e.Graphics.Clear(TransparentOverlayBackgroundColor);
                using var brush = new SolidBrush(Color.White);
                using var font = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 1), FontStyle.Bold);
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                var textRect = new Rectangle(4, 4, Math.Max(1, Width - 8), Math.Max(1, Height - 8));
                TextRenderer.DrawText(
                    e.Graphics,
                    "Overlay render issue. Check logs.",
                    font,
                    textRect,
                    brush.Color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                {
                    Log.Error($"Overlay paint stack trace: {ex.StackTrace}");
                }
            }
            catch (Exception fallbackEx)
            {
                Log.Error($"Overlay fallback rendering failure: {fallbackEx}");
            }
        }
    }

    private void DrawHideStackIcon(Graphics graphics)
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconScale = GetOverlayIconScale();
        var iconReferenceBounds = _label.Bounds;
        if (iconReferenceBounds.IsEmpty || iconReferenceBounds.Height <= 0)
            iconReferenceBounds = GetHideStackIconReferenceBounds();
        var referenceCenterY = iconReferenceBounds.Top + (iconReferenceBounds.Height / 2);
        var baseIconY = referenceCenterY - (iconHeight / 2);
        var iconY = Math.Max(0, Math.Min(Height - iconHeight - 2, baseIconY + HideStackIconVerticalOffsetPx));
        var iconRenderWidth = Math.Max(
            HideStackIconMinWidth,
            iconHeight + HideStackIconHorizontalPaddingPx + HideStackIconPaddingPx);
        var helloBoxOffsetPx = _showHelloTextFrame
            ? HelloHideStackIconHorizontalOffsetPx
            : 0;
        var iconLeft = Math.Max(
            0,
            Math.Min(
                Padding.Left + HideStackIconHorizontalOffsetPx + helloBoxOffsetPx,
                Width - iconRenderWidth - Math.Max(1, HideStackIconHorizontalOffsetPx)));
        var iconBounds = new Rectangle(
            iconLeft,
            iconY,
            Math.Max(1, iconRenderWidth),
            Math.Max(1, Math.Min(iconHeight, Height - iconY - 2)));
        var scaledIconBounds = ScaleBoundsAroundCenter(iconBounds, iconScale);
        var scaledIconHeight = Math.Max(1, scaledIconBounds.Height);

        using var iconBgPath = CreateRoundedRectanglePath(scaledIconBounds, HideStackIconCornerRadius);
        using var iconBgBrush = new SolidBrush(HideStackIconFillColor);
        using var iconBgPen = new Pen(HideStackIconStrokeColor, Math.Max(1.0f, Math.Min(2.6f, scaledIconHeight / 12.0f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.FillPath(iconBgBrush, iconBgPath);
        graphics.DrawPath(iconBgPen, iconBgPath);

        var clickableBounds = new Rectangle(
            scaledIconBounds.Left,
            scaledIconBounds.Top,
            Math.Max(1, scaledIconBounds.Width),
            Math.Max(1, scaledIconBounds.Height));

        if (!DrawNerdFontIcon(graphics, scaledIconBounds, HideStackIconGlyphColor, _hideStackIconGlyph))
        {
            var glyphInset = Math.Max(HideStackIconMinInset + 1, scaledIconHeight / 5);
            var glyphSize = Math.Max(2, scaledIconBounds.Height - (glyphInset * 2));
            var glyphBounds = new Rectangle(
                scaledIconBounds.Left + ((scaledIconBounds.Width - glyphSize) / 2),
                scaledIconBounds.Top + ((scaledIconBounds.Height - glyphSize) / 2),
                Math.Max(1, glyphSize),
                Math.Max(1, glyphSize));
            var lineColor = HideStackIconGlyphColor;
            var lineWidth = Math.Max(2.0f, Math.Min(4.2f, scaledIconHeight / 7.0f));
            using var iconPen = new Pen(lineColor, lineWidth)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            var margin = Math.Max(2, (int)Math.Round(glyphSize * 0.18));
            graphics.DrawLine(
                iconPen,
                glyphBounds.Left + margin,
                glyphBounds.Top + margin,
                glyphBounds.Right - margin,
                glyphBounds.Bottom - margin);
            graphics.DrawLine(
                iconPen,
                glyphBounds.Left + margin,
                glyphBounds.Bottom - margin,
                glyphBounds.Right - margin,
                glyphBounds.Top + margin);
        }

        _hideStackIconBounds = clickableBounds;
        LogHideStackBounds();
    }

    private void DrawStopListeningIcon(Graphics graphics, int existingRightReservedPx = 0)
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconScale = GetOverlayIconScale();
        var iconReferenceBounds = _label.Bounds;
        if (iconReferenceBounds.IsEmpty || iconReferenceBounds.Height <= 0)
            iconReferenceBounds = GetHideStackIconReferenceBounds();
        var referenceCenterY = iconReferenceBounds.Top + (iconReferenceBounds.Height / 2);
        var baseIconY = referenceCenterY - (iconHeight / 2);
        var iconY = Math.Max(0, Math.Min(Height - iconHeight - 2, baseIconY + StopListeningIconVerticalOffsetPx));

        var iconRenderWidth = Math.Max(
            StopListeningIconMinWidth,
            iconHeight + StopListeningIconHorizontalPaddingPx + StopListeningIconPaddingPx);
        var desiredLeft = Width - iconRenderWidth - Math.Max(1, StopListeningIconHorizontalOffsetPx + existingRightReservedPx);
        var iconLeft = Math.Max(
            0,
            Math.Min(
                desiredLeft,
                Width - Math.Max(1, iconRenderWidth)));

        var iconBounds = new Rectangle(
            iconLeft,
            iconY,
            Math.Max(1, iconRenderWidth),
            Math.Max(1, Math.Min(iconHeight, Height - iconY - 2)));
        var scaledIconBounds = ScaleBoundsAroundCenter(iconBounds, iconScale);
        var scaledIconHeight = Math.Max(1, scaledIconBounds.Height);

        using var iconBgPath = CreateRoundedRectanglePath(scaledIconBounds, StopListeningIconCornerRadius);
        using var iconBgBrush = new SolidBrush(StopListeningIconFillColor);
        using var iconBgPen = new Pen(StopListeningIconStrokeColor, Math.Max(1.0f, Math.Min(3.0f, scaledIconHeight / 12.0f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.FillPath(iconBgBrush, iconBgPath);
        graphics.DrawPath(iconBgPen, iconBgPath);

        var clickableBounds = new Rectangle(
            scaledIconBounds.Left,
            scaledIconBounds.Top,
            Math.Max(1, scaledIconBounds.Width),
            Math.Max(1, scaledIconBounds.Height));

        if (!DrawNerdFontIcon(graphics, scaledIconBounds, StopListeningIconGlyphColor, _stopListeningIconGlyph))
        {
            var iconColor = StopListeningIconGlyphColor;
            var iconSize = Math.Max(
                2,
                Math.Min(scaledIconBounds.Width, scaledIconBounds.Height)
                    - (Math.Max(StopListeningIconMinInset + 1, scaledIconHeight / 5) * 2));
            var iconSquare = new Rectangle(
                scaledIconBounds.Left + ((scaledIconBounds.Width - iconSize) / 2),
                scaledIconBounds.Top + ((scaledIconBounds.Height - iconSize) / 2),
                Math.Max(2, iconSize),
                Math.Max(2, iconSize));
            using var iconBrush = new SolidBrush(iconColor);
            graphics.FillRectangle(iconBrush, iconSquare);
        }

        _stopListeningIconBounds = clickableBounds;
    }

    private void DrawCancelListeningIcon(Graphics graphics, int existingRightReservedPx = 0)
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconScale = GetOverlayIconScale();
        var iconReferenceBounds = _label.Bounds;
        if (iconReferenceBounds.IsEmpty || iconReferenceBounds.Height <= 0)
            iconReferenceBounds = GetHideStackIconReferenceBounds();
        var referenceCenterY = iconReferenceBounds.Top + (iconReferenceBounds.Height / 2);
        var baseIconY = referenceCenterY - (iconHeight / 2);
        var iconY = Math.Max(0, Math.Min(Height - iconHeight - 2, baseIconY + CancelListeningIconVerticalOffsetPx));

        var iconRenderWidth = Math.Max(
            CancelListeningIconMinWidth,
            iconHeight + CancelListeningIconHorizontalPaddingPx + CancelListeningIconPaddingPx);
        var desiredLeft = Width - iconRenderWidth - Math.Max(1, CancelListeningIconHorizontalOffsetPx + existingRightReservedPx);
        var iconLeft = Math.Max(
            0,
            Math.Min(
                desiredLeft,
                Width - Math.Max(1, iconRenderWidth)));

        var iconBounds = new Rectangle(
            iconLeft,
            iconY,
            Math.Max(1, iconRenderWidth),
            Math.Max(1, Math.Min(iconHeight, Height - iconY - 2)));
        var scaledIconBounds = ScaleBoundsAroundCenter(iconBounds, iconScale);
        var scaledIconHeight = Math.Max(1, scaledIconBounds.Height);

        using var iconBgPath = CreateRoundedRectanglePath(scaledIconBounds, StopListeningIconCornerRadius);
        using var iconBgBrush = new SolidBrush(CancelListeningIconFillColor);
        using var iconBgPen = new Pen(CancelListeningIconStrokeColor, Math.Max(1.0f, Math.Min(3.0f, scaledIconHeight / 12.0f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.FillPath(iconBgBrush, iconBgPath);
        graphics.DrawPath(iconBgPen, iconBgPath);

        var clickableBounds = new Rectangle(
            scaledIconBounds.Left,
            scaledIconBounds.Top,
            Math.Max(1, scaledIconBounds.Width),
            Math.Max(1, scaledIconBounds.Height));

        if (!DrawNerdFontIcon(graphics, scaledIconBounds, CancelListeningIconGlyphColor, _cancelListeningIconGlyph))
        {
            var iconColor = CancelListeningIconGlyphColor;
            var glyphInset = Math.Max(CancelListeningIconMinInset + 1, scaledIconHeight / 5);
            using var iconPen = new Pen(iconColor, Math.Max(2.0f, Math.Min(4.0f, scaledIconHeight / 7.0f)))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var glyphBounds = new Rectangle(
                scaledIconBounds.Left + glyphInset,
                scaledIconBounds.Top + glyphInset,
                Math.Max(2, scaledIconBounds.Width - (glyphInset * 2)),
                Math.Max(2, scaledIconBounds.Height - (glyphInset * 2)));

            graphics.DrawLine(iconPen, glyphBounds.Left, glyphBounds.Top, glyphBounds.Right, glyphBounds.Bottom);
            graphics.DrawLine(iconPen, glyphBounds.Left, glyphBounds.Bottom, glyphBounds.Right, glyphBounds.Top);
        }

        _cancelListeningIconBounds = clickableBounds;
    }

    private void DrawStartListeningIcon(Graphics graphics, int existingRightReservedPx = 0)
    {
        var iconHeight = GetOverlayControlIconReferenceHeight();
        var iconScale = GetOverlayIconScale();
        var iconReferenceBounds = _label.Bounds;
        if (iconReferenceBounds.IsEmpty || iconReferenceBounds.Height <= 0)
            iconReferenceBounds = GetHideStackIconReferenceBounds();
        var referenceCenterY = iconReferenceBounds.Top + (iconReferenceBounds.Height / 2);
        var baseIconY = referenceCenterY - (iconHeight / 2);
        var iconY = Math.Max(0, Math.Min(Height - iconHeight - 2, baseIconY + StartListeningIconVerticalOffsetPx));

        var iconRenderWidth = Math.Max(
            StartListeningIconMinWidth,
            iconHeight + StartListeningIconHorizontalPaddingPx + StartListeningIconPaddingPx);
        var desiredLeft = Width - iconRenderWidth - Math.Max(1, StartListeningIconHorizontalOffsetPx + existingRightReservedPx);
        var iconLeft = Math.Max(
            0,
            Math.Min(
                desiredLeft,
                Width - Math.Max(1, iconRenderWidth)));

        var iconBounds = new Rectangle(
            iconLeft,
            iconY,
            Math.Max(1, iconRenderWidth),
            Math.Max(1, Math.Min(iconHeight, Height - iconY - 2)));
        var scaledIconBounds = ScaleBoundsAroundCenter(iconBounds, iconScale);
        var scaledIconHeight = Math.Max(1, scaledIconBounds.Height);

        using var iconBgPath = CreateRoundedRectanglePath(scaledIconBounds, StartListeningIconCornerRadius);
        using var iconBgBrush = new SolidBrush(StartListeningIconFillColor);
        using var iconBgPen = new Pen(StartListeningIconStrokeColor, Math.Max(1.0f, Math.Min(3.0f, scaledIconHeight / 12.0f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.FillPath(iconBgBrush, iconBgPath);
        graphics.DrawPath(iconBgPen, iconBgPath);

        var clickableBounds = new Rectangle(
            scaledIconBounds.Left,
            scaledIconBounds.Top,
            Math.Max(1, scaledIconBounds.Width),
            Math.Max(1, scaledIconBounds.Height));

        if (!DrawNerdFontIcon(graphics, scaledIconBounds, StartListeningIconGlyphColor, _startListeningIconGlyph))
        {
            var iconColor = StartListeningIconGlyphColor;
            var lineWidth = Math.Max(2.0f, Math.Min(4.2f, scaledIconHeight / 7.0f));
            using var iconPen = new Pen(iconColor, lineWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            var glyphInset = Math.Max(StartListeningIconMinInset + 1, scaledIconHeight / 5);
            var glyphBounds = new Rectangle(
                scaledIconBounds.Left + glyphInset,
                scaledIconBounds.Top + glyphInset,
                Math.Max(2, scaledIconBounds.Width - (glyphInset * 2)),
                Math.Max(2, scaledIconBounds.Height - (glyphInset * 2)));

            var headSize = Math.Max(4, Math.Min(glyphBounds.Width, Math.Max(6, glyphBounds.Height / 2)));
            var stemHeight = Math.Max(2, Math.Min(
                glyphBounds.Height / 2 + glyphBounds.Height / 6,
                glyphBounds.Height - (glyphBounds.Height / 4)));
            var centerX = glyphBounds.Left + (glyphBounds.Width / 2);
            var topY = glyphBounds.Top + ((glyphBounds.Height - stemHeight - headSize) / 2);
            var headRect = new Rectangle(
                centerX - (headSize / 2),
                topY,
                headSize,
                headSize);
            var stemTop = headRect.Bottom;
            var stemBottom = stemTop + stemHeight;

            var baseY = Math.Min(stemBottom, glyphBounds.Bottom - Math.Max(2, headSize / 3));

            graphics.DrawArc(
                iconPen,
                headRect.Left,
                headRect.Top,
                headRect.Width,
                headRect.Height,
                200,
                140);
            graphics.DrawLine(
                iconPen,
                centerX,
                stemTop,
                centerX,
                baseY);
            var standY = Math.Min(glyphBounds.Bottom - 1, baseY + Math.Max(2, (int)Math.Ceiling(lineWidth * 1.5f)));
            graphics.DrawLine(
                iconPen,
                centerX - headSize / 2,
                standY,
                centerX + headSize / 2,
                standY);
        }

        _startListeningIconBounds = clickableBounds;
    }

    private Rectangle GetHideStackIconReferenceBounds()
    {
        var boundsToUse = _label.Bounds;
        if (boundsToUse.IsEmpty || boundsToUse.Height <= 0)
        {
            return new Rectangle(
                Padding.Left,
                Padding.Top,
                Math.Max(1, ClientSize.Width - Padding.Horizontal),
                Math.Max(1, ClientSize.Height - Padding.Vertical));
        }

        var mergedBounds = boundsToUse;
        if (_actionLabel.Visible && !string.IsNullOrWhiteSpace(_actionLabel.Text))
        {
            mergedBounds = Rectangle.Union(mergedBounds, _actionLabel.Bounds);
        }

        if (_prefixLabel.Visible && !string.IsNullOrWhiteSpace(_prefixLabel.Text))
        {
            mergedBounds = Rectangle.Union(mergedBounds, _prefixLabel.Bounds);
        }

        return mergedBounds;
    }

    private void LogHideStackBounds()
    {
        if (_hideStackIconBounds.IsEmpty || !_showHideStackIcon)
            return;

        var now = DateTime.UtcNow;
        if (_activeMessageId == _lastLoggedHideStackMessageId && now < _nextHideStackPositionLogAt)
            return;

        _lastLoggedHideStackMessageId = _activeMessageId;
        _nextHideStackPositionLogAt = now.AddSeconds(1);

        Log.Info(
            $"Hide-stack positioning | messageId={_activeMessageId}, text='{_label.Text}', " +
            $"labelBounds={_label.Bounds.X},{_label.Bounds.Y},{_label.Bounds.Width},{_label.Bounds.Height}, " +
            $"hideIconBounds={_hideStackIconBounds.X},{_hideStackIconBounds.Y},{_hideStackIconBounds.Width},{_hideStackIconBounds.Height}, " +
            $"formBounds={Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}");
    }

    private void DrawHelloTextFrame(Graphics graphics)
    {
        if (string.IsNullOrWhiteSpace(_label.Text))
            return;

        var frame = Rectangle.Inflate(
            _label.Bounds,
            HelloTextFramePaddingPx,
            HelloTextFramePaddingPx);
        frame.Intersect(new Rectangle(0, 0, Width, Height));
        if (frame.IsEmpty)
            return;

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var framePen = new Pen(HelloTextFrameColor, HelloTextFrameWidth);
        graphics.DrawRectangle(framePen, frame);
    }

    private void DrawFullWidthText(Graphics graphics)
    {
        DrawFullWidthTextBlock(_label.Text, _label.Font, _label.Bounds, _label.ForeColor, graphics);
        if (_lastShowActionLine)
            DrawFullWidthTextBlock(_actionLabel.Text, _actionLabel.Font, _actionLabel.Bounds, _actionLabel.ForeColor, graphics);

        if (_lastShowPrefixLine)
            DrawFullWidthTextBlock(_prefixLabel.Text, _prefixLabel.Font, _prefixLabel.Bounds, _prefixLabel.ForeColor, graphics);
    }

    private void DrawFallbackFullWidthText(Graphics graphics)
    {
        var alignmentFlags = GetTextAlignmentFlags(_lastTextAlign);
        var textFlags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | alignmentFlags;

        DrawTextRendererLine(graphics, _label.Text, _label.Font, _label.ForeColor, _label.Bounds, textFlags);
        if (_lastShowActionLine)
            DrawTextRendererLine(graphics, _actionLabel.Text, _actionLabel.Font, _actionLabel.ForeColor, _actionLabel.Bounds, textFlags);

        if (_lastShowPrefixLine)
            DrawTextRendererLine(graphics, _prefixLabel.Text, _prefixLabel.Font, _prefixLabel.ForeColor, _prefixLabel.Bounds, textFlags);
    }

    private void DrawTextRendererLine(
        Graphics graphics,
        string? text,
        Font font,
        Color color,
        Rectangle bounds,
        TextFormatFlags flags)
    {
        if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var safeBounds = new Rectangle(
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height));
        try
        {
            TextRenderer.DrawText(graphics, text, font, safeBounds, color, flags);
        }
        catch
        {
            try
            {
                TextRenderer.DrawText(graphics, text, font, safeBounds, color, TextFormatFlags.Default | TextFormatFlags.NoPadding);
            }
            catch (Exception fallbackEx)
            {
                Log.Error($"TextRenderer fallback failed for label line. Message={fallbackEx.Message}, Flags={flags}");
            }
        }
    }

    private static TextFormatFlags GetTextAlignmentFlags(ContentAlignment textAlign)
    {
        var flags = textAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.MiddleLeft or ContentAlignment.BottomLeft
                => TextFormatFlags.Left,
            ContentAlignment.TopRight or ContentAlignment.MiddleRight or ContentAlignment.BottomRight
                => TextFormatFlags.Right,
            _ => TextFormatFlags.HorizontalCenter
        };

        flags |= textAlign switch
        {
            ContentAlignment.TopLeft or ContentAlignment.TopCenter or ContentAlignment.TopRight
                => TextFormatFlags.Top,
            ContentAlignment.BottomLeft or ContentAlignment.BottomCenter or ContentAlignment.BottomRight
                => TextFormatFlags.Bottom,
            _ => TextFormatFlags.VerticalCenter
        };

        return flags;
    }

    private void DrawOverlayWarningIcons(Graphics graphics)
    {
        if (_showHideStackIcon)
            DrawHideStackIcon(graphics);

        var rightReservedPx = 0;
        if (_showStopListeningIcon)
        {
            DrawStopListeningIcon(graphics, rightReservedPx);
            rightReservedPx += GetStopListeningIconReservePx();
        }

        if (_showCancelListeningIcon)
        {
            DrawCancelListeningIcon(graphics, rightReservedPx);
            rightReservedPx += GetCancelListeningIconReservePx();
        }

        if (_showStartListeningIcon)
            DrawStartListeningIcon(graphics, rightReservedPx);
    }

    private void DrawFullWidthTextBlock(string? text, Font font, Rectangle bounds, Color color, Graphics graphics)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var lines = BuildFullWidthLines(graphics, text, font, Math.Max(1, bounds.Width));
        if (lines.Count == 0)
            return;

        var lineHeight = Math.Max(1, GetTextHeight(graphics, "M", font, Math.Max(1, bounds.Width)));
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
        DrawStringWithTextRendererFallback(
            graphics,
            text,
            font,
            brush,
            new RectangleF(
                Math.Max(0, bounds.Left),
                Math.Max(0, y),
                Math.Max(1, bounds.Width),
                Math.Max(1, lineHeight)),
            TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
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
            DrawStringWithTextRendererFallback(
                graphics,
                words[i],
                font,
                brush,
                new RectangleF(Math.Max(0, cursorX), Math.Max(0, y), Math.Max(1, bounds.Width), Math.Max(1, lineHeight)),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            cursorX += wordSizes[i];
            if (i + 1 >= words.Length)
                continue;

            cursorX += spaceWidth + baseExtra + (i < remainder ? 1 : 0);
        }
    }

    private void DrawStringWithTextRendererFallback(
        Graphics graphics,
        string text,
        Font font,
        Brush brush,
        RectangleF layout,
        TextFormatFlags textFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var safeLayout = Rectangle.FromLTRB(
            (int)Math.Round(layout.Left),
            (int)Math.Round(layout.Top),
            (int)Math.Round(layout.Left + Math.Max(1, layout.Width)),
            (int)Math.Round(layout.Top + Math.Max(1, layout.Height)));

        var drawColor = (brush as SolidBrush)?.Color ?? Color.White;
        try
        {
            TextRenderer.DrawText(
                graphics,
                text,
                font,
                safeLayout,
                drawColor,
                textFlags);
        }
        catch
        {
            Log.Error($"Text rendering fallback failed. Text='{text}', Bounds={safeLayout}, Font={font?.Name ?? "null"}, Height={font?.Height.ToString() ?? "null"}");
            // Final fallback keeps behavior predictable even if flags/options are unsupported.
            try
            {
                TextRenderer.DrawText(
                    graphics,
                    text,
                    font,
                    safeLayout,
                    drawColor,
                    TextFormatFlags.Default | TextFormatFlags.NoPadding);
            }
            catch (Exception hardFallbackEx)
            {
                Log.Error($"Text rendering hard fallback failed. Text='{text}', Bounds={safeLayout}, Message={hardFallbackEx.Message}");
            }
        }
    }

    private static int MeasureTextWidth(Graphics graphics, string text, Font font)
    {
        try
        {
            var size = TextRenderer.MeasureText(
                graphics,
                text,
                font,
                new Size(int.MaxValue / 4, int.MaxValue / 4),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            return size.Width;
        }
        catch
        {
            return Math.Max(0, Math.Min(int.MaxValue / 4, text.Length * 10));
        }
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

        var lines = (_label.Text ?? string.Empty).Split('\n');

        using var meterFont = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 1), FontStyle.Bold);

        var firstLineHeight = Math.Max(
            meterFont.Height,
            GetTextHeight(graphics, lines.Length > 0 ? lines[0] : string.Empty, meterFont, labelArea.Width));
        var secondLineHeight = lines.Length > 1
            ? GetTextHeight(graphics, lines[1], meterFont, labelArea.Width)
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

    private int GetTextHeight(Graphics graphics, string text, Font font, int maxWidth)
    {
        try
        {
            return TextRenderer.MeasureText(
                graphics,
                text,
                font,
                new Size(Math.Max(1, maxWidth), int.MaxValue),
                TextFormatFlags.NoPrefix).Height;
        }
        catch (Exception ex)
        {
            Log.Error($"Listening level meter text measurement failed. Message={ex.Message}");
            using var fallbackFont = CreateOverlayFont(Math.Max(10, _overlayFontSizePt - 1), FontStyle.Bold);
            return TextRenderer.MeasureText(
                graphics,
                text,
                fallbackFont,
                new Size(Math.Max(1, maxWidth), int.MaxValue),
                TextFormatFlags.NoPrefix).Height;
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

        if (HandleStartListeningIconTap(sender, e))
            return;

        if (HandleStopListeningIconTap(sender, e))
            return;

        if (HandleCancelListeningIconTap(sender, e))
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

    private bool HandleStartListeningIconTap(object? sender, MouseEventArgs e)
    {
        if (!_showStartListeningIcon)
            return false;

        var clickPoint = TranslateToFormClientPoint(sender, e.Location);
        if (_startListeningIconBounds.IsEmpty || !_startListeningIconBounds.Contains(clickPoint))
            return false;

        _tapHandledForCurrentPress = true;
        OverlayStartListeningIconTapped?.Invoke(this, new OverlayStartListeningIconTappedEventArgs(_activeMessageId));
        return true;
    }

    private bool HandleStopListeningIconTap(object? sender, MouseEventArgs e)
    {
        if (!_showStopListeningIcon)
            return false;

        var clickPoint = TranslateToFormClientPoint(sender, e.Location);
        if (_stopListeningIconBounds.IsEmpty || !_stopListeningIconBounds.Contains(clickPoint))
            return false;

        _tapHandledForCurrentPress = true;
        OverlayStopListeningIconTapped?.Invoke(
            this,
            new OverlayStopListeningIconTappedEventArgs(_activeMessageId));
        return true;
    }

    private bool HandleCancelListeningIconTap(object? sender, MouseEventArgs e)
    {
        if (!_showCancelListeningIcon)
            return false;

        var clickPoint = TranslateToFormClientPoint(sender, e.Location);
        if (_cancelListeningIconBounds.IsEmpty || !_cancelListeningIconBounds.Contains(clickPoint))
            return false;

        _tapHandledForCurrentPress = true;
        OverlayCancelListeningIconTapped?.Invoke(
            this,
            new OverlayCancelListeningIconTappedEventArgs(_activeMessageId));
        return true;
    }

    private Point TranslateToFormClientPoint(object? sender, Point point)
    {
        return sender is Control control
            ? ((Control)this).PointToClient(control.PointToScreen(point))
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

public sealed class OverlayStartListeningIconTappedEventArgs : EventArgs
{
    public OverlayStartListeningIconTappedEventArgs(int messageId)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }
}

public sealed class OverlayStopListeningIconTappedEventArgs : EventArgs
{
    public OverlayStopListeningIconTappedEventArgs(int messageId)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }
}

public sealed class OverlayCancelListeningIconTappedEventArgs : EventArgs
{
    public OverlayCancelListeningIconTappedEventArgs(int messageId)
    {
        MessageId = messageId;
    }

    public int MessageId { get; }
}
