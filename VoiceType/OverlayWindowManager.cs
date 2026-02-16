using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoiceType;

public sealed class OverlayWindowManager : IOverlayManager
{
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
        public string? OverlayKey { get; set; }
    }

    private readonly Func<OverlayForm> _overlayFactory;
    private readonly bool _suppressAutoHide = true;
    private readonly Dictionary<OverlayForm, ManagedOverlay> _activeOverlays = new();
    private readonly Dictionary<string, OverlayForm> _overlaysByKey = new(StringComparer.Ordinal);
    private readonly List<OverlayForm> _stackOrder = [];
    private readonly object _sync = new();
    private readonly int _baseWidthClampMin = 260;
    private readonly int _horizontalScreenPadding = 2;

    private int _stackSequence;
    private int _globalMessageId;
    private int _overlayOpacityPercent = AppConfig.DefaultOverlayOpacityPercent;
    private int _overlayWidthPercent = AppConfig.DefaultOverlayWidthPercent;
    private int _overlayFontSizePt = AppConfig.DefaultOverlayFontSizePt;
    private bool _overlayShowBorder = true;

    public OverlayWindowManager(Func<OverlayForm>? overlayFactory = null)
    {
        _overlayFactory = overlayFactory ?? (() => new OverlayForm());
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
        bool autoPosition = true)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var globalMessageId = 0;
        lock (_sync)
        {
            if (!string.IsNullOrWhiteSpace(overlayKey) &&
                TryGetActiveOverlay(overlayKey, out var managed))
            {
                globalMessageId = ++_globalMessageId;
                var localMessageId = managed.Form.ShowMessage(
                    text,
                    color,
                    ComputeDurationMs(durationMs),
                    textAlign,
                    centerTextBlock,
                    _suppressAutoHide ? false : showCountdownBar,
                    tapToCancel,
                    remoteActionText,
                    remoteActionColor,
                    prefixText,
                    prefixColor,
                    autoPosition);
                if (localMessageId == 0)
                return 0;

                managed.GlobalMessageId = globalMessageId;
                managed.LocalMessageId = localMessageId;
                if (trackInStack)
                    RepositionVisibleOverlaysLocked();

                return globalMessageId;
            }

            globalMessageId = ++_globalMessageId;
            var managedOverlay = CreateOverlay(text, color, durationMs,
                textAlign, centerTextBlock, showCountdownBar, tapToCancel, remoteActionText,
                remoteActionColor, prefixText, prefixColor, overlayKey, autoPosition);
            if (managedOverlay is null)
                return 0;

            managedOverlay.GlobalMessageId = globalMessageId;
            managedOverlay.LocalMessageId = managedOverlay.Form.ShowMessage(
                text,
                color,
                ComputeDurationMs(durationMs),
                textAlign,
                centerTextBlock,
                _suppressAutoHide ? false : showCountdownBar,
                tapToCancel,
                remoteActionText,
                remoteActionColor,
                prefixText,
                prefixColor,
                autoPosition);

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
        var delay = Math.Max(0, delayBetweenMs);
        List<OverlayForm> orderedOverlays;
        lock (_sync)
        {
            orderedOverlays = _stackOrder
                .Where(x => _activeOverlays.TryGetValue(x, out var managed) &&
                            !managed.Form.IsDisposed &&
                            managed.Form.Visible)
                .OrderByDescending(x => _activeOverlays[x].Sequence)
                .ToList();
        }

        for (var index = 0; index < orderedOverlays.Count; index++)
        {
            orderedOverlays[index].FadeOut(index * delay);
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
            _stackOrder.Clear();
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
        bool autoPosition)
    {
        var overlay = _overlayFactory();
        if (!autoPosition)
            overlay.Location = Point.Empty;
        overlay.ApplyHudSettings(
            _overlayOpacityPercent,
            _overlayWidthPercent,
            _overlayFontSizePt,
            _overlayShowBorder);

        var managed = new ManagedOverlay(overlay, ++_stackSequence, globalMessageId: 0, overlayKey: overlayKey);
        overlay.VisibleChanged += OnOverlayVisibleChanged;
        overlay.OverlayTapped += OnOverlayTapped;

        _activeOverlays.Add(overlay, managed);
        _stackOrder.Add(overlay);
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
        if (!_activeOverlays.Remove(overlay, out _))
            return;

        _stackOrder.Remove(overlay);
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
        var visibleOverlays = _stackOrder
            .Where(x => _activeOverlays.TryGetValue(x, out var managed) &&
                        !managed.Form.IsDisposed &&
                        managed.Form.Visible)
            .OrderBy(x => _activeOverlays[x].Sequence)
            .Select(x => x)
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

    private int ComputeDurationMs(int durationMs)
    {
        return _suppressAutoHide
            ? 0
            : Math.Max(0, durationMs);
    }
}
