using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoiceType;

public sealed class OverlayWindowManager : IOverlayManager
{
    private sealed class OverlayStackSpine
    {
        private readonly List<ManagedOverlay> _stack = [];

        public void Register(ManagedOverlay managedOverlay)
        {
            if (_stack.Contains(managedOverlay))
                return;

            _stack.Add(managedOverlay);
        }

        public void Remove(ManagedOverlay managedOverlay)
        {
            _stack.Remove(managedOverlay);
        }

        public IReadOnlyList<ManagedOverlay> GetTrackedOverlays()
        {
            return _stack
                .Where(x => !x.Form.IsDisposed && x.Form.Visible && x.TrackInStack)
                .OrderBy(x => x.Sequence)
                .ToList();
        }

        public IReadOnlyList<ManagedOverlay> GetTrackedOverlaysTopToBottom()
        {
            return GetTrackedOverlays()
                .OrderByDescending(x => x.Sequence)
                .ToList();
        }

        public void Clear()
        {
            _stack.Clear();
        }
    }

    private sealed class ManagedOverlay
    {
        public ManagedOverlay(OverlayForm form, int sequence, int globalMessageId, string? overlayKey)
        {
            Form = form;
            Sequence = sequence;
            GlobalMessageId = globalMessageId;
            OverlayKey = overlayKey;
        }

        public OverlayForm Form { get; }
        public int Sequence { get; set; }
        public int GlobalMessageId { get; set; }
        public int LocalMessageId { get; set; }
        public bool TrackInStack { get; set; } = true;
        public string? OverlayKey { get; set; }
        public bool IsRemoteAction { get; set; }
        public bool IsClipboardCopyAction { get; set; }
        public bool IsSubmittedAction { get; set; }
    }

    private readonly Func<OverlayForm> _overlayFactory;
    private readonly Dictionary<OverlayForm, ManagedOverlay> _activeOverlays = new();
    private readonly Dictionary<string, OverlayForm> _overlaysByKey = new(StringComparer.Ordinal);
    private readonly OverlayStackSpine _stackSpine = new();
    private readonly object _sync = new();
    private readonly int _baseWidthClampMin = 260;
    private readonly int _horizontalScreenPadding = 2;
    private int _stackHorizontalOffsetPx;

    private int _stackSequence;
    private int _globalMessageId;
    private int _overlayOpacityPercent = AppConfig.DefaultOverlayOpacityPercent;
    private int _overlayWidthPercent = AppConfig.DefaultOverlayWidthPercent;
    private int _overlayFontSizePt = AppConfig.DefaultOverlayFontSizePt;
    private bool _overlayShowBorder = true;
    private int _overlayBackgroundMode = AppConfig.DefaultOverlayBackgroundMode;
    private int _overlayFadeProfile = AppConfig.DefaultOverlayFadeProfile;
    private int _fadeDelayBetweenOverlaysMs;
    private int _overlayFadeDurationMs;
    private int _overlayFadeTickIntervalMs;
    private OverlayForm? _activeCopyTapBorderOverlay;
    private int _suppressStackEmptyNotificationDepth;

    public OverlayWindowManager(Func<OverlayForm>? overlayFactory = null)
    {
        _overlayFactory = overlayFactory ?? (() => new OverlayForm());
        var profile = GetFadeProfile(AppConfig.DefaultOverlayFadeProfile);
        _fadeDelayBetweenOverlaysMs = profile.DelayBetweenOverlaysMs;
        _overlayFadeDurationMs = profile.FadeDurationMs;
        _overlayFadeTickIntervalMs = profile.FadeTickIntervalMs;
    }

    public event EventHandler<int>? OverlayTapped;
    public event EventHandler<OverlayCopyTappedEventArgs>? OverlayCopyTapped;
    public event EventHandler<OverlayCountdownPlaybackIconTappedEventArgs>? OverlayCountdownPlaybackIconTapped;
    public event EventHandler<OverlayHideStackIconTappedEventArgs>? OverlayHideStackIconTapped;
    public event EventHandler<OverlayStartListeningIconTappedEventArgs>? OverlayStartListeningIconTapped;
    public event EventHandler<OverlayStopListeningIconTappedEventArgs>? OverlayStopListeningIconTapped;
    public event EventHandler<OverlayCancelListeningIconTappedEventArgs>? OverlayCancelListeningIconTapped;
    public event EventHandler? OverlayStackEmptied;

    private static string GetOverlayDebugLabel(ManagedOverlay managed)
    {
        var isDisposed = managed?.Form.IsDisposed == true;
        var visible = managed is not null && managed.Form.Visible;
        return managed is null
            ? "<unknown>"
            : $"seq={managed.Sequence},global={managed.GlobalMessageId},local={managed.LocalMessageId}," +
              $"key={managed.OverlayKey ?? "<none>"}," +
              $"track={managed.TrackInStack},visible={visible},disposed={isDisposed}," +
              $"remote={managed.IsRemoteAction},copy={managed.IsClipboardCopyAction},submitted={managed.IsSubmittedAction}," +
              $"size={managed.Form.Width}x{managed.Form.Height}";
    }

    private void LogOverlayStackSnapshot(string reason)
    {
        List<string> stackEntries;
        int activeCount;
        int trackedCount;
        lock (_sync)
        {
            activeCount = _activeOverlays.Count;
            trackedCount = _stackSpine.GetTrackedOverlays().Count;
            stackEntries = _stackSpine.GetTrackedOverlays()
                .OrderBy(x => x.Sequence)
                .Select(GetOverlayDebugLabel)
                .ToList();
        }

        if (stackEntries.Count == 0)
        {
            Log.Info($"Overlay stack snapshot ({reason}): active={activeCount}, tracked={trackedCount}, entries=none");
            return;
        }

        Log.Info(
            $"Overlay stack snapshot ({reason}): active={activeCount}, tracked={trackedCount}, " +
            $"entries=[{string.Join(" | ", stackEntries)}]");
    }

    public int ShowMessage(
        string text,
        Color? color = null,
        int durationMs = 3000,
        ContentAlignment textAlign = ContentAlignment.MiddleCenter,
        bool centerTextBlock = false,
        bool showCountdownBar = false,
        bool tapToCancel = false,
        string? remoteActionText = null,
        Color? remoteActionColor = null,
        string? prefixText = null,
        Color? prefixColor = null,
        string? overlayKey = null,
        bool trackInStack = true,
        bool autoPosition = true,
        bool autoHide = false,
        bool isRemoteAction = false,
        bool isClipboardCopyAction = false,
        bool allowCopyTap = true,
        bool animateHide = false,
        bool showListeningLevelMeter = false,
        int listeningLevelPercent = 0,
        string? copyText = null,
        bool isSubmittedAction = false,
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
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        Log.Info(
            $"ShowMessage request: key={overlayKey ?? "<none>"}, textLen={text.Length}, " +
            $"track={trackInStack}, autoPosition={autoPosition}, autoHide={autoHide}, duration={durationMs}, " +
            $"countdown={showCountdownBar}, remote={isRemoteAction}, copyAction={isClipboardCopyAction}, allowCopyTap={allowCopyTap}, submitted={isSubmittedAction}");
        LogOverlayStackSnapshot($"show-message-start:{overlayKey ?? "<none>"}");

        var effectiveCountdownBar = showCountdownBar;
        var globalMessageId = 0;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(overlayKey) &&
                TryGetActiveOverlay(overlayKey, out var managed))
            {
                LogOverlayStackSnapshot($"show-message-update-before:{overlayKey ?? "<none>"}");
                globalMessageId = ++_globalMessageId;
                var wasTrackedInStack = managed.TrackInStack;
                var effectiveDurationMs = autoHide
                    ? ComputeDurationMs(durationMs, autoHide)
                    : durationMs;

                var localMessageId = managed.Form.ShowMessage(
                    text,
                    color,
                    effectiveDurationMs,
                    textAlign,
                    centerTextBlock,
                    effectiveCountdownBar,
                    tapToCancel,
                    remoteActionText,
                    remoteActionColor,
                    prefixText,
                    prefixColor,
                    autoPosition,
                    animateHide,
                    autoHide,
                    showListeningLevelMeter,
                    listeningLevelPercent,
                    allowCopyTap && !isClipboardCopyAction,
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
                    cancelListeningIconGlyph);
                if (localMessageId == 0)
                    return 0;

                managed.GlobalMessageId = globalMessageId;
                managed.LocalMessageId = localMessageId;
                managed.TrackInStack = trackInStack;
                managed.IsClipboardCopyAction = isClipboardCopyAction;
                managed.IsSubmittedAction = isSubmittedAction;
                Log.Info(
                    $"ShowMessage updated overlay key={overlayKey}, global={globalMessageId}, local={managed.LocalMessageId}, " +
                    $"seq={managed.Sequence}, wasTracked={wasTrackedInStack}, nowTracked={managed.TrackInStack}");
                LogOverlayStackSnapshot($"show-message-update-after:{overlayKey ?? "<none>"}");
                if (wasTrackedInStack || trackInStack)
                    RepositionVisibleOverlaysLocked();

                return globalMessageId;
            }

            globalMessageId = ++_globalMessageId;
            LogOverlayStackSnapshot($"show-message-create-before:{overlayKey ?? "<none>"}");
            Log.Info($"ShowMessage creating overlay key={overlayKey ?? "<none>"}, global={globalMessageId}");
            var managedOverlay = CreateOverlay(text, color, durationMs,
                textAlign, centerTextBlock, showCountdownBar, tapToCancel, remoteActionText,
                remoteActionColor, prefixText, prefixColor, overlayKey, trackInStack,
                isRemoteAction, isClipboardCopyAction, isSubmittedAction);
            if (managedOverlay is null)
            {
                Log.Info("ShowMessage failed to create overlay.");
                return 0;
            }

            managedOverlay.GlobalMessageId = globalMessageId;
            var createDurationMs = autoHide
                ? ComputeDurationMs(durationMs, autoHide)
                : durationMs;

                managedOverlay.LocalMessageId = managedOverlay.Form.ShowMessage(
                text,
                color,
                createDurationMs,
                textAlign,
                centerTextBlock,
                effectiveCountdownBar,
                tapToCancel,
                remoteActionText,
                remoteActionColor,
                prefixText,
                prefixColor,
                autoPosition,
                animateHide,
                autoHide,
                showListeningLevelMeter,
                listeningLevelPercent,
                allowCopyTap && !isClipboardCopyAction,
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
                cancelListeningIconGlyph);

            if (managedOverlay.LocalMessageId == 0)
            {
                RemoveOverlayLocked(managedOverlay.Form);
                return 0;
            }
            Log.Info(
                $"ShowMessage created overlay key={overlayKey ?? "<none>"}, global={globalMessageId}, " +
                $"local={managedOverlay.LocalMessageId}, seq={managedOverlay.Sequence}, track={trackInStack}");
            LogOverlayStackSnapshot($"show-message-create-after:{overlayKey ?? "<none>"}");
            if (trackInStack)
                RepositionVisibleOverlaysLocked();

            return globalMessageId;
        }
    }

    public void ApplyHudSettings(int opacityPercent, int widthPercent, int fontSizePt, bool showBorder, int overlayBackgroundMode)
    {
        _overlayOpacityPercent = AppConfig.NormalizeOverlayOpacityPercent(opacityPercent);
        _overlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent(widthPercent);
        _overlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt(fontSizePt);
        _overlayShowBorder = showBorder;
        _overlayBackgroundMode = AppConfig.NormalizeOverlayBackgroundMode(overlayBackgroundMode);

        lock (_sync)
        {
            foreach (var overlay in _activeOverlays.Keys)
            {
                if (overlay.IsDisposed)
                    continue;

                overlay.ApplyHudSettings(
                    _overlayOpacityPercent,
                    _overlayWidthPercent,
                    _overlayFontSizePt,
                    _overlayShowBorder,
                    _overlayBackgroundMode);
            }
        }
    }

    public void ApplyFadeProfile(int overlayFadeProfile)
    {
        _overlayFadeProfile = AppConfig.NormalizeOverlayFadeProfile(overlayFadeProfile);
        var profile = GetFadeProfile(_overlayFadeProfile);
        _fadeDelayBetweenOverlaysMs = profile.DelayBetweenOverlaysMs;
        _overlayFadeDurationMs = profile.FadeDurationMs;
        _overlayFadeTickIntervalMs = profile.FadeTickIntervalMs;
    }

    public bool HasTrackedOverlays()
    {
        lock (_sync)
        {
            return _stackSpine.GetTrackedOverlays().Count > 0;
        }
    }

    public bool HasTrackedOverlay(string overlayKey)
    {
        if (string.IsNullOrWhiteSpace(overlayKey))
            return false;

        lock (_sync)
        {
            if (!_overlaysByKey.TryGetValue(overlayKey, out var overlay))
                return false;

            if (overlay.IsDisposed)
                return false;

            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return false;

            return managed.Form.Visible && managed.TrackInStack;
        }
    }

    public bool TryGetOverlayKey(int globalMessageId, out string? overlayKey)
    {
        overlayKey = null;
        if (globalMessageId <= 0)
            return false;

        lock (_sync)
        {
            var matching = _activeOverlays.Values.FirstOrDefault(
                overlay => overlay.GlobalMessageId == globalMessageId && overlay.Form.Visible);
            if (matching is null)
                return false;

            overlayKey = matching.OverlayKey;
            return true;
        }
    }

    public void ResetTrackedStack()
    {
        List<OverlayForm> overlaysToDispose;
        lock (_sync)
        {
            LogOverlayStackSnapshot("reset-before");
            overlaysToDispose = _activeOverlays.Keys
                .Where(x => !x.IsDisposed)
                .ToList();
            Log.Info(
                $"ResetTrackedStack: active={overlaysToDispose.Count}, stack={_stackSpine.GetTrackedOverlays().Count}");

            _activeOverlays.Clear();
            _overlaysByKey.Clear();
            _stackSpine.Clear();
            _activeCopyTapBorderOverlay = null;
            _globalMessageId = 0;
            _stackSequence = 0;
            _suppressStackEmptyNotificationDepth = 0;
        }

        foreach (var overlay in overlaysToDispose)
        {
            UnhookOverlay(overlay);
            overlay.Dispose();
        }

        LogOverlayStackSnapshot("reset-after");
    }

    public void ApplyCountdownPlaybackIcon(string? countdownPlaybackIcon)
    {
        List<OverlayForm> trackedOverlays;
        lock (_sync)
        {
            trackedOverlays = _activeOverlays.Keys
                .Where(x => !x.IsDisposed)
                .ToList();
        }

        foreach (var overlay in trackedOverlays)
            overlay.ApplyCountdownPlaybackIcon(countdownPlaybackIcon);
    }

    public void HideAll(bool suppressStackEmptyNotification = false)
    {
        Log.Info($"HideAll invoked: suppressStackEmptyNotification={suppressStackEmptyNotification}");
        List<OverlayForm> overlaysToHide;
        lock (_sync)
        {
            overlaysToHide = _stackSpine
                .GetTrackedOverlaysTopToBottom()
                .Where(managed => _activeOverlays.ContainsKey(managed.Form))
                .Select(managed => managed.Form)
                .ToList();
        }
        Log.Info($"HideAll candidate count={overlaysToHide.Count}");

        if (suppressStackEmptyNotification)
        {
            lock (_sync)
            {
                _suppressStackEmptyNotificationDepth++;
            }
        }

        if (overlaysToHide.Count == 0 && suppressStackEmptyNotification)
        {
            lock (_sync)
            {
                _suppressStackEmptyNotificationDepth = Math.Max(
                    0,
                    _suppressStackEmptyNotificationDepth - 1);
            }
            return;
        }

        FadeOverlaysTopToBottom(overlaysToHide);
    }

    public void ClearCountdownBar(string overlayKey)
    {
        if (string.IsNullOrWhiteSpace(overlayKey))
            return;

        lock (_sync)
        {
            if (!_overlaysByKey.TryGetValue(overlayKey, out var overlay))
                return;

            if (overlay.IsDisposed)
                return;

            overlay.ClearCountdownBar();
        }
    }

    public void FadeVisibleOverlaysTopToBottom(int delayBetweenMs = 140)
    {
        Log.Info($"FadeVisibleOverlaysTopToBottom requested (delay={delayBetweenMs}, profile={_overlayFadeProfile})");
        var delay = delayBetweenMs < 0
            ? 0
            : delayBetweenMs;

        if (_overlayFadeProfile == AppConfig.OffOverlayFadeProfile)
            return;

        if (_overlayFadeDurationMs <= 0)
            return;

        if (delay <= 0)
            delay = Math.Max(0, _fadeDelayBetweenOverlaysMs);

        List<ManagedOverlay> orderedOverlays;
        lock (_sync)
        {
            orderedOverlays = _stackSpine
                .GetTrackedOverlaysTopToBottom()
                .Where(managed => _activeOverlays.ContainsKey(managed.Form))
                .ToList();
        }
        Log.Info($"FadeVisibleOverlaysTopToBottom candidate count={orderedOverlays.Count}");

        for (var index = 0; index < orderedOverlays.Count; index++)
        {
            orderedOverlays[index].Form.FadeOut(
                index * delay,
                _overlayFadeDurationMs,
                _overlayFadeTickIntervalMs);
        }
    }

    public int GetStackHorizontalOffset()
    {
        lock (_sync)
        {
            return _stackHorizontalOffsetPx;
        }
    }

    public void SetStackHorizontalOffset(int offsetPx)
    {
        lock (_sync)
        {
            _stackHorizontalOffsetPx = offsetPx;
        }

        RepositionVisibleOverlaysLocked();
    }

    public void HideOverlay(string overlayKey)
    {
        Log.Info($"HideOverlay called: key={overlayKey ?? "<none>"}");
        if (string.IsNullOrWhiteSpace(overlayKey))
            return;

        List<OverlayForm> overlaysToHide;
        lock (_sync)
        {
            overlaysToHide = _overlaysByKey
                .Where(pair => string.Equals(pair.Key, overlayKey, StringComparison.Ordinal))
                .Select(pair => pair.Value)
                .Distinct()
                .ToList();
        }

        if (overlaysToHide.Count == 0)
            return;
        Log.Info($"HideOverlay candidate count={overlaysToHide.Count}");

        FadeOverlaysTopToBottom(
            ReorderOverlaysForDismissal(overlaysToHide)
                .ToList());
    }

    public void HideOverlays(IEnumerable<string> overlayKeys)
    {
        if (overlayKeys is null)
            return;

        var requestedKeys = overlayKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct()
            .ToList();

        if (requestedKeys.Count == 0)
            return;

        var overlaysToHide = new List<OverlayForm>();
        lock (_sync)
        {
            foreach (var key in requestedKeys)
            {
                overlaysToHide.AddRange(
                    _overlaysByKey
                        .Where(pair => string.Equals(pair.Key, key, StringComparison.Ordinal))
                        .Select(pair => pair.Value));
            }
        }

        if (overlaysToHide.Count == 0)
            return;

        FadeOverlaysTopToBottom(
            ReorderOverlaysForDismissal(overlaysToHide));
    }

    public void DismissRemoteActionOverlays()
    {
        List<ManagedOverlay> remoteOverlays;
        lock (_sync)
        {
            remoteOverlays = _stackSpine
                .GetTrackedOverlaysTopToBottom()
                .Where(managed => managed.IsRemoteAction && _activeOverlays.ContainsKey(managed.Form))
                .ToList();
        }

        if (remoteOverlays.Count == 0)
            return;

        var fadeDurationMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 260
            : Math.Max(1, _overlayFadeDurationMs);
        var fadeDelayMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 40
            : Math.Max(0, _fadeDelayBetweenOverlaysMs);
        var fadeTickIntervalMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 16
            : Math.Clamp(_overlayFadeTickIntervalMs, 8, 200);

        for (var index = 0; index < remoteOverlays.Count; index++)
        {
            remoteOverlays[index].Form.FadeOut(
                index * fadeDelayMs,
                fadeDurationMs,
                fadeTickIntervalMs);
        }
    }

    public void DismissSubmittedActionOverlays(int keepGlobalMessageId = 0)
    {
        List<ManagedOverlay> submittedOverlays;
        lock (_sync)
        {
            submittedOverlays = _stackSpine
                .GetTrackedOverlaysTopToBottom()
                .Where(managed => managed.IsSubmittedAction
                    && managed.GlobalMessageId != keepGlobalMessageId
                    && _activeOverlays.ContainsKey(managed.Form))
                .ToList();
        }

        if (submittedOverlays.Count == 0)
            return;

        var fadeDurationMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 260
            : Math.Max(1, _overlayFadeDurationMs);
        var fadeDelayMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 40
            : Math.Max(0, _fadeDelayBetweenOverlaysMs);
        var fadeTickIntervalMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 16
            : Math.Clamp(_overlayFadeTickIntervalMs, 8, 200);

        for (var index = 0; index < submittedOverlays.Count; index++)
        {
            submittedOverlays[index].Form.FadeOut(
                index * fadeDelayMs,
                fadeDurationMs,
                fadeTickIntervalMs);
        }
    }

    public void DismissCopyActionOverlays(int keepGlobalMessageId = 0)
    {
        List<ManagedOverlay> copyOverlays;
        lock (_sync)
        {
            copyOverlays = _stackSpine
                .GetTrackedOverlaysTopToBottom()
                .Where(managed => managed.IsClipboardCopyAction
                    && managed.GlobalMessageId != keepGlobalMessageId
                    && _activeOverlays.ContainsKey(managed.Form))
                .ToList();
        }

        if (copyOverlays.Count == 0)
            return;

        var fadeDurationMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 260
            : Math.Max(1, _overlayFadeDurationMs);
        var fadeDelayMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 40
            : Math.Max(0, _fadeDelayBetweenOverlaysMs);
        var fadeTickIntervalMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 16
            : Math.Clamp(_overlayFadeTickIntervalMs, 8, 200);

        for (var index = 0; index < copyOverlays.Count; index++)
        {
            copyOverlays[index].Form.FadeOut(
                index * fadeDelayMs,
                fadeDurationMs,
                fadeTickIntervalMs);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var overlay in _activeOverlays.Keys.ToList())
            {
                UnhookOverlay(overlay);
                overlay.Dispose();
            }

            _activeOverlays.Clear();
            _stackSpine.Clear();
            _overlaysByKey.Clear();
            _stackHorizontalOffsetPx = 0;
        }
    }

    private ManagedOverlay? CreateOverlay(
        string text,
        Color? color,
        int durationMs,
        ContentAlignment textAlign,
        bool centerTextBlock,
        bool showCountdownBar,
        bool tapToCancel,
        string? remoteActionText,
        Color? remoteActionColor,
        string? prefixText,
        Color? prefixColor,
        string? overlayKey,
        bool trackInStack,
        bool isRemoteAction,
        bool isClipboardCopyAction,
        bool isSubmittedAction)
    {
        Log.Info(
            $"CreateOverlay: key={overlayKey ?? "<none>"}, textLen={text.Length}, duration={durationMs}, " +
            $"track={trackInStack}, remote={isRemoteAction}, copyAction={isClipboardCopyAction}, submitted={isSubmittedAction}");
        var overlay = _overlayFactory();
        overlay.ApplyHudSettings(
            _overlayOpacityPercent,
            _overlayWidthPercent,
            _overlayFontSizePt,
            _overlayShowBorder,
            _overlayBackgroundMode);

        var managed = new ManagedOverlay(overlay, ++_stackSequence, globalMessageId: 0, overlayKey: overlayKey)
        {
            TrackInStack = trackInStack,
            IsRemoteAction = isRemoteAction,
            IsClipboardCopyAction = isClipboardCopyAction,
            IsSubmittedAction = isSubmittedAction
        };
        overlay.VisibleChanged += OnOverlayVisibleChanged;
        overlay.OverlayTapped += OnOverlayTapped;
        overlay.OverlayCopyTapped += OnOverlayCopyTapped;
        overlay.OverlayCountdownPlaybackIconTapped += OnOverlayCountdownPlaybackIconTapped;
        overlay.OverlayHideStackIconTapped += OnOverlayHideStackIconTapped;
        overlay.OverlayStartListeningIconTapped += OnOverlayStartListeningIconTapped;
        overlay.OverlayStopListeningIconTapped += OnOverlayStopListeningIconTapped;
        overlay.OverlayCancelListeningIconTapped += OnOverlayCancelListeningIconTapped;
        overlay.OverlayHorizontalDragged += OnOverlayHorizontalDragged;

        _activeOverlays.Add(overlay, managed);
        _stackSpine.Register(managed);
        if (!string.IsNullOrWhiteSpace(overlayKey))
            _overlaysByKey[overlayKey] = overlay;
        Log.Info(
            $"CreateOverlay registered: global={managed.GlobalMessageId}, seq={managed.Sequence}, " +
            $"stackTracked={_stackSpine.GetTrackedOverlays().Count}");

        return managed;
    }

    private bool TryGetActiveOverlay(string overlayKey, out ManagedOverlay managedOverlay)
    {
        managedOverlay = default!;
        if (!_overlaysByKey.TryGetValue(overlayKey, out var overlay))
            return false;

        if (!TryGetManagedOverlay(overlay, out managedOverlay))
            return false;

        if (managedOverlay.Form.IsDisposed || !managedOverlay.Form.Visible)
            return false;

        return true;
    }

    private bool TryGetManagedOverlay(OverlayForm overlay, out ManagedOverlay managed)
    {
        return _activeOverlays.TryGetValue(overlay, out managed!);
    }

    private void OnOverlayVisibleChanged(object? sender, EventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        if (overlay.Visible)
            return;

        int globalMessageId = 0;
        lock (_sync)
        {
            if (_activeOverlays.TryGetValue(overlay, out var managed))
                globalMessageId = managed.GlobalMessageId;
        }

        Log.Info($"Overlay visibility changed to false: global={globalMessageId}, formVisible={overlay.Visible}");

        lock (_sync)
        {
            if (!_activeOverlays.ContainsKey(overlay))
                return;

            RemoveOverlayLocked(overlay);
            RepositionVisibleOverlaysLocked();
        }
    }

    private void OnOverlayTapped(object? sender, OverlayTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        OverlayTapped?.Invoke(this, globalMessageId);
    }

    private void OnOverlayCopyTapped(object? sender, OverlayCopyTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        ApplyCopyTapBorder(overlay);
        OverlayCopyTapped?.Invoke(this, new OverlayCopyTappedEventArgs(globalMessageId, e.CopiedText));
    }

    private void OnOverlayCountdownPlaybackIconTapped(object? sender, OverlayCountdownPlaybackIconTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        OverlayCountdownPlaybackIconTapped?.Invoke(
            this,
            new OverlayCountdownPlaybackIconTappedEventArgs(globalMessageId, e.IconText));
    }

    private void OnOverlayHideStackIconTapped(object? sender, OverlayHideStackIconTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        OverlayHideStackIconTapped?.Invoke(this, new OverlayHideStackIconTappedEventArgs(globalMessageId));
    }

    private void OnOverlayStartListeningIconTapped(object? sender, OverlayStartListeningIconTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        OverlayStartListeningIconTapped?.Invoke(
            this,
            new OverlayStartListeningIconTappedEventArgs(globalMessageId));
    }

    private void OnOverlayStopListeningIconTapped(object? sender, OverlayStopListeningIconTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        OverlayStopListeningIconTapped?.Invoke(
            this,
            new OverlayStopListeningIconTappedEventArgs(globalMessageId));
    }

    private void OnOverlayCancelListeningIconTapped(object? sender, OverlayCancelListeningIconTappedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        int globalMessageId;
        lock (_sync)
        {
            if (!_activeOverlays.TryGetValue(overlay, out var managed))
                return;

            if (managed.LocalMessageId != e.MessageId)
                return;

            globalMessageId = managed.GlobalMessageId;
        }

        OverlayCancelListeningIconTapped?.Invoke(
            this,
            new OverlayCancelListeningIconTappedEventArgs(globalMessageId));
    }

    private void OnOverlayHorizontalDragged(object? sender, OverlayHorizontalDraggedEventArgs e)
    {
        if (sender is not OverlayForm overlay)
            return;

        if (!_activeOverlays.ContainsKey(overlay))
            return;

        if (e.DeltaX == 0)
            return;

        _stackHorizontalOffsetPx += e.DeltaX;
        RepositionVisibleOverlaysLocked();
    }

    private void RemoveOverlayLocked(OverlayForm overlay)
    {
        bool shouldNotifyStackEmpty = false;
        bool suppressStackEmptyNotification = false;
        lock (_sync)
        {
            var trackedBefore = _stackSpine.GetTrackedOverlays().Count;
            if (!_activeOverlays.Remove(overlay, out var managed))
                return;

            if (ReferenceEquals(_activeCopyTapBorderOverlay, overlay))
            {
                _activeCopyTapBorderOverlay = null;
                overlay.SetCopyTapBorderVisible(false);
            }

            _stackSpine.Remove(managed);
            var overlayKeys = _overlaysByKey
                .Where(pair => ReferenceEquals(pair.Value, overlay))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in overlayKeys)
                _overlaysByKey.Remove(key);
            UnhookOverlay(overlay);
            overlay.Dispose();
            var trackedAfter = _stackSpine.GetTrackedOverlays().Count;
            Log.Info(
                $"Overlay removed: global={managed.GlobalMessageId}, local={managed.LocalMessageId}, key={managed.OverlayKey ?? "<none>"}, " +
                $"trackedBefore={trackedBefore}, trackedAfter={trackedAfter}");

            shouldNotifyStackEmpty = _stackSpine.GetTrackedOverlays().Count == 0;
            if (shouldNotifyStackEmpty && _suppressStackEmptyNotificationDepth > 0)
            {
                _suppressStackEmptyNotificationDepth--;
                suppressStackEmptyNotification = true;
            }
        }

        if (shouldNotifyStackEmpty && !suppressStackEmptyNotification)
            OverlayStackEmptied?.Invoke(this, EventArgs.Empty);
    }

    private List<OverlayForm> ReorderOverlaysForDismissal(IEnumerable<OverlayForm> overlays)
    {
        var orderedOverlays = _stackSpine
            .GetTrackedOverlaysTopToBottom()
            .Where(managed => overlays.Contains(managed.Form))
            .Select(managed => managed.Form)
            .Distinct()
            .ToList();

        foreach (var overlay in overlays)
        {
            if (!orderedOverlays.Contains(overlay))
                orderedOverlays.Add(overlay);
        }

        return orderedOverlays;
    }

    private void FadeOverlaysTopToBottom(IEnumerable<OverlayForm> overlays)
    {
        var overlaysToHide = overlays?
            .Distinct()
            .ToList();

        if (overlaysToHide == null || overlaysToHide.Count == 0)
            return;

        var fadeDelayMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 40
            : Math.Max(0, _fadeDelayBetweenOverlaysMs);

        for (var index = 0; index < overlaysToHide.Count; index++)
        {
            FadeAndDismissOverlay(
                overlaysToHide[index],
                index * fadeDelayMs);
        }
    }

    private void FadeAndDismissOverlay(OverlayForm overlay, int delayMs = 0)
    {
        if (overlay is null)
        {
            Log.Info("FadeAndDismissOverlay ignored: overlay was null");
            return;
        }

        if (overlay.IsDisposed)
        {
            RemoveOverlayLocked(overlay);
            return;
        }

        if (!overlay.Visible)
        {
            RemoveOverlayLocked(overlay);
            return;
        }

        var fadeDurationMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 280
            : Math.Max(1, _overlayFadeDurationMs);
        var fadeTickIntervalMs = _overlayFadeProfile == AppConfig.OffOverlayFadeProfile
            ? 16
            : Math.Clamp(_overlayFadeTickIntervalMs, 8, 200);
        Log.Info(
            $"FadeAndDismissOverlay: delay={delayMs}, duration={fadeDurationMs}, " +
            $"tickInterval={fadeTickIntervalMs}");

        overlay.FadeOut(Math.Max(0, delayMs), fadeDurationMs, fadeTickIntervalMs);
    }

    private void UnhookOverlay(OverlayForm overlay)
    {
        overlay.VisibleChanged -= OnOverlayVisibleChanged;
        overlay.OverlayTapped -= OnOverlayTapped;
        overlay.OverlayCopyTapped -= OnOverlayCopyTapped;
        overlay.OverlayCountdownPlaybackIconTapped -= OnOverlayCountdownPlaybackIconTapped;
        overlay.OverlayHideStackIconTapped -= OnOverlayHideStackIconTapped;
        overlay.OverlayStartListeningIconTapped -= OnOverlayStartListeningIconTapped;
        overlay.OverlayStopListeningIconTapped -= OnOverlayStopListeningIconTapped;
        overlay.OverlayCancelListeningIconTapped -= OnOverlayCancelListeningIconTapped;
        overlay.OverlayHorizontalDragged -= OnOverlayHorizontalDragged;
    }

    private void ApplyCopyTapBorder(OverlayForm overlay)
    {
        if (ReferenceEquals(_activeCopyTapBorderOverlay, overlay))
            return;

        ClearCopyTapBorder();
        _activeCopyTapBorderOverlay = overlay;
        overlay.SetCopyTapBorderVisible(true);
    }

    private void ClearCopyTapBorder()
    {
        if (_activeCopyTapBorderOverlay is null)
            return;

        _activeCopyTapBorderOverlay.SetCopyTapBorderVisible(false);
        _activeCopyTapBorderOverlay = null;
    }

    private void RepositionVisibleOverlaysLocked()
    {
        Log.Info($"Reposition overlays requested");
        var visibleOverlays = _stackSpine
            .GetTrackedOverlays()
            .Where(managed => _activeOverlays.ContainsKey(managed.Form))
            .ToList();

        if (visibleOverlays.Count == 0)
            return;

        var totalHeight = visibleOverlays.Sum(x => x.Form.Height)
            + (visibleOverlays.Count - 1) * 0;
        Log.Info($"Reposition overlays: count={visibleOverlays.Count}, totalHeight={totalHeight}, topSeq={visibleOverlays[0].Sequence}, bottomSeq={visibleOverlays[^1].Sequence}");

        // Keep overlays anchored to the primary working area for deterministic visibility.
        var workingArea = Screen.PrimaryScreen?.WorkingArea
            ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        if (workingArea.IsEmpty)
            workingArea = Screen.AllScreens.FirstOrDefault(s => !s.Bounds.IsEmpty)?.WorkingArea ?? Rectangle.Empty;

        if (workingArea.IsEmpty)
            return;
        var maximumStackHeight = Math.Max(0, workingArea.Height - 4);
        const int interOverlaySpacing = 0;

        while (visibleOverlays.Count > 0)
        {
            var neededHeight = visibleOverlays.Sum(x => x.Form.Height)
                + Math.Max(0, (visibleOverlays.Count - 1) * interOverlaySpacing);

            if (neededHeight <= maximumStackHeight)
                break;

            var removableOverlay = visibleOverlays
                .FirstOrDefault(ShouldEvictAsUnnecessaryWhenStackOverflows);

            if (removableOverlay is null)
            {
                // Fall back to removing the oldest overlay at the bottom of the stack.
                removableOverlay = visibleOverlays[0];
            }

            visibleOverlays.Remove(removableOverlay);
            FadeAndDismissOverlay(removableOverlay.Form);
        }
        Log.Info($"Reposition overlays after overflow trim count={visibleOverlays.Count}");

        var cursorY = workingArea.Bottom - 4;
            foreach (var managed in visibleOverlays)
            {
                var overlay = managed.Form;
                var width = Math.Clamp(overlay.Width, _baseWidthClampMin, Math.Max(_baseWidthClampMin, workingArea.Width - 24));
            var centeredX = workingArea.Left + ((workingArea.Width - width) / 2);
            var x = Math.Clamp(
                centeredX + _stackHorizontalOffsetPx,
                workingArea.Left + _horizontalScreenPadding,
                Math.Max(workingArea.Left + _horizontalScreenPadding, workingArea.Right - width - _horizontalScreenPadding));

                cursorY -= overlay.Height;
                overlay.Size = new Size(width, overlay.Height);
                overlay.Location = new Point(x, cursorY);
                cursorY -= interOverlaySpacing;
            }
        LogOverlayStackSnapshot("reposition-complete");
    }

    private static bool ShouldEvictAsUnnecessaryWhenStackOverflows(ManagedOverlay managedOverlay)
    {
        if (managedOverlay is null)
            return false;

        return managedOverlay.IsRemoteAction
            || managedOverlay.IsClipboardCopyAction
            || managedOverlay.IsSubmittedAction;
    }

    private int ComputeDurationMs(int durationMs, bool autoHide)
    {
        return autoHide
            ? Math.Max(0, durationMs)
            : 0;
    }

    private static (int DelayBetweenOverlaysMs, int FadeDurationMs, int FadeTickIntervalMs) GetFadeProfile(int profileId)
    {
        return profileId switch
        {
            AppConfig.OffOverlayFadeProfile => (0, 0, 0),
            AppConfig.FastOverlayFadeProfile => (45, 260, 16),
            AppConfig.GentleOverlayFadeProfile => (220, 900, 24),
            _ => (120, 520, 40) // Balanced/default
        };
    }

}
