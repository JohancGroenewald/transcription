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
    }

    private readonly Func<OverlayForm> _overlayFactory;
    private readonly Dictionary<OverlayForm, ManagedOverlay> _activeOverlays = new();
    private readonly Dictionary<string, OverlayForm> _overlaysByKey = new(StringComparer.Ordinal);
    private readonly OverlayStackSpine _stackSpine = new();
    private readonly object _sync = new();
    private readonly int _baseWidthClampMin = 260;
    private readonly int _horizontalScreenPadding = 2;

    private int _stackSequence;
    private int _globalMessageId;
    private int _overlayOpacityPercent = AppConfig.DefaultOverlayOpacityPercent;
    private int _overlayWidthPercent = AppConfig.DefaultOverlayWidthPercent;
    private int _overlayFontSizePt = AppConfig.DefaultOverlayFontSizePt;
    private bool _overlayShowBorder = true;
    private int _overlayFadeProfile = AppConfig.DefaultOverlayFadeProfile;
    private int _fadeDelayBetweenOverlaysMs;
    private int _overlayFadeDurationMs;
    private int _overlayFadeTickIntervalMs;

    public OverlayWindowManager(Func<OverlayForm>? overlayFactory = null)
    {
        _overlayFactory = overlayFactory ?? (() => new OverlayForm());
        var profile = GetFadeProfile(AppConfig.DefaultOverlayFadeProfile);
        _fadeDelayBetweenOverlaysMs = profile.DelayBetweenOverlaysMs;
        _overlayFadeDurationMs = profile.FadeDurationMs;
        _overlayFadeTickIntervalMs = profile.FadeTickIntervalMs;
    }

    public event EventHandler<int>? OverlayTapped;

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
        bool autoHide = true,
        bool animateHide = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var effectiveCountdownBar = autoHide && showCountdownBar;
        var globalMessageId = 0;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(overlayKey) &&
                TryGetActiveOverlay(overlayKey, out var managed))
            {
                globalMessageId = ++_globalMessageId;
                var wasTrackedInStack = managed.TrackInStack;
                var localMessageId = managed.Form.ShowMessage(
                    text,
                    color,
                    ComputeDurationMs(durationMs, autoHide),
                    textAlign,
                    centerTextBlock,
                    effectiveCountdownBar,
                    tapToCancel,
                    remoteActionText,
                    remoteActionColor,
                    prefixText,
                    prefixColor,
                    autoPosition,
                    animateHide);
                if (localMessageId == 0)
                    return 0;

                managed.GlobalMessageId = globalMessageId;
                managed.LocalMessageId = localMessageId;
                managed.TrackInStack = trackInStack;
                if (wasTrackedInStack || trackInStack)
                    RepositionVisibleOverlaysLocked();

                return globalMessageId;
            }

            globalMessageId = ++_globalMessageId;
            var managedOverlay = CreateOverlay(text, color, durationMs,
                textAlign, centerTextBlock, showCountdownBar, tapToCancel, remoteActionText,
                remoteActionColor, prefixText, prefixColor, overlayKey, trackInStack);
            if (managedOverlay is null)
                return 0;

            managedOverlay.GlobalMessageId = globalMessageId;
            managedOverlay.LocalMessageId = managedOverlay.Form.ShowMessage(
                text,
                color,
                ComputeDurationMs(durationMs, autoHide),
                textAlign,
                centerTextBlock,
                effectiveCountdownBar,
                tapToCancel,
                remoteActionText,
                remoteActionColor,
                prefixText,
                prefixColor,
                autoPosition,
                animateHide);

            if (managedOverlay.LocalMessageId == 0)
            {
                RemoveOverlayLocked(managedOverlay.Form);
                return 0;
            }
            if (trackInStack)
                RepositionVisibleOverlaysLocked();

            return globalMessageId;
        }
    }

    public void ApplyHudSettings(int opacityPercent, int widthPercent, int fontSizePt, bool showBorder)
    {
        _overlayOpacityPercent = AppConfig.NormalizeOverlayOpacityPercent(opacityPercent);
        _overlayWidthPercent = AppConfig.NormalizeOverlayWidthPercent(widthPercent);
        _overlayFontSizePt = AppConfig.NormalizeOverlayFontSizePt(fontSizePt);
        _overlayShowBorder = showBorder;

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
                    _overlayShowBorder);
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

    public void HideAll()
    {
        lock (_sync)
        {
            foreach (var overlay in _activeOverlays.Keys.ToList())
            {
                if (!overlay.IsDisposed && overlay.Visible)
                    overlay.Hide();
            }
        }
    }

    public void FadeVisibleOverlaysTopToBottom(int delayBetweenMs = 140)
    {
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

        for (var index = 0; index < orderedOverlays.Count; index++)
        {
            orderedOverlays[index].Form.FadeOut(
                index * delay,
                _overlayFadeDurationMs,
                _overlayFadeTickIntervalMs);
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
        bool trackInStack)
    {
        var overlay = _overlayFactory();
        overlay.ApplyHudSettings(
            _overlayOpacityPercent,
            _overlayWidthPercent,
            _overlayFontSizePt,
            _overlayShowBorder);

        var managed = new ManagedOverlay(overlay, ++_stackSequence, globalMessageId: 0, overlayKey: overlayKey)
        {
            TrackInStack = trackInStack
        };
        overlay.VisibleChanged += OnOverlayVisibleChanged;
        overlay.OverlayTapped += OnOverlayTapped;

        _activeOverlays.Add(overlay, managed);
        _stackSpine.Register(managed);
        if (!string.IsNullOrWhiteSpace(overlayKey))
            _overlaysByKey[overlayKey] = overlay;

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

    private void RemoveOverlayLocked(OverlayForm overlay)
    {
        if (!_activeOverlays.Remove(overlay, out var managed))
            return;

        _stackSpine.Remove(managed);
        var overlayKeys = _overlaysByKey
            .Where(pair => ReferenceEquals(pair.Value, overlay))
            .Select(pair => pair.Key)
            .ToList();
        foreach (var key in overlayKeys)
            _overlaysByKey.Remove(key);
        UnhookOverlay(overlay);
        overlay.Dispose();
    }

    private void UnhookOverlay(OverlayForm overlay)
    {
        overlay.VisibleChanged -= OnOverlayVisibleChanged;
        overlay.OverlayTapped -= OnOverlayTapped;
    }

    private void RepositionVisibleOverlaysLocked()
    {
        var visibleOverlays = _stackSpine
            .GetTrackedOverlays()
            .Where(managed => _activeOverlays.ContainsKey(managed.Form))
            .Select(managed => managed.Form)
            .ToList();

        if (visibleOverlays.Count == 0)
            return;

        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var cursorY = workingArea.Bottom - 4;
        foreach (var overlay in visibleOverlays)
        {
            var width = Math.Clamp(overlay.Width, _baseWidthClampMin, Math.Max(_baseWidthClampMin, workingArea.Width - 24));
            var x = Math.Clamp(
                workingArea.Left + ((workingArea.Width - width) / 2),
                workingArea.Left + _horizontalScreenPadding,
                Math.Max(workingArea.Left + _horizontalScreenPadding, workingArea.Right - width - _horizontalScreenPadding));

            cursorY -= overlay.Height;
            overlay.Size = new Size(width, overlay.Height);
            overlay.Location = new Point(x, cursorY);
            cursorY -= 4;
        }
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
