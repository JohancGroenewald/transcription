using System.Windows.Forms;

namespace VoiceType;

internal sealed class OverlayStackWindowManager
{
    private readonly IOverlayManager _overlayManager;
    private readonly Func<bool> _isShuttingDown;
    private readonly Func<bool> _isShutdownRequested;
    private readonly Func<bool> _isTranscriptionReady;
    private readonly Control _uiDispatcher;
    private readonly Action<string> _log;
    private bool _hiddenByUser;

    public OverlayStackWindowManager(
        IOverlayManager overlayManager,
        Func<bool> isShuttingDown,
        Func<bool> isShutdownRequested,
        Func<bool> isTranscriptionReady,
        Control uiDispatcher,
        Action<string> log)
    {
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _isShuttingDown = isShuttingDown ?? throw new ArgumentNullException(nameof(isShuttingDown));
        _isShutdownRequested = isShutdownRequested ?? throw new ArgumentNullException(nameof(isShutdownRequested));
        _isTranscriptionReady = isTranscriptionReady ?? throw new ArgumentNullException(nameof(isTranscriptionReady));
        _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public bool IsHiddenByUser => _hiddenByUser;

    public bool HasTrackedOverlays => _overlayManager.HasTrackedOverlays();

    public bool HasTrackedOverlay(string overlayKey)
    {
        if (string.IsNullOrWhiteSpace(overlayKey))
            return false;

        return _overlayManager.HasTrackedOverlay(overlayKey);
    }

    public void MarkHiddenByUser()
    {
        if (_hiddenByUser)
        {
            _log("Stack hidden-by-user flag already true.");
            return;
        }

        _hiddenByUser = true;
        _log("Stack hidden-by-user flag set.");
    }

    public void ClearHiddenByUser()
    {
        if (!_hiddenByUser)
        {
            _log("Stack hidden-by-user flag already false.");
            return;
        }

        _hiddenByUser = false;
        _log("Stack hidden-by-user flag cleared.");
    }

    public void HideStack(string reason)
    {
        MarkHiddenByUser();
        _log($"Stack hide requested. reason={reason}");
        _overlayManager.HideAll(suppressStackEmptyNotification: true);
    }

    public void ShowStack(string reason, string helloOverlayKey, Action seedHelloOverlay)
    {
        _log($"Stack show requested. reason={reason}");
        RestoreAfterReactivation(reason, helloOverlayKey, seedHelloOverlay);
        PositionStack(reason);
    }

    public void PositionStack(string reason)
    {
        _log($"Stack reposition requested. reason={reason}");
        _overlayManager.SetStackHorizontalOffset(_overlayManager.GetStackHorizontalOffset());
    }

    public void Startup(string reason, Action seedHelloOverlay)
    {
        if (_isShuttingDown() || _isShutdownRequested())
        {
            _log($"Stack manager ignore ({reason}) because app is shutting down.");
            return;
        }

        if (!_isTranscriptionReady())
        {
            _log($"Stack manager ignore ({reason}) because transcription service is not ready.");
            return;
        }

        _log($"Stack manager ({reason}) reset + reseed hello overlay.");
        _overlayManager.ResetTrackedStack();
        seedHelloOverlay();
    }

    public void HandleStackEmpty(string reason, string helloOverlayKey, Action seedHelloOverlay)
    {
        _log($"Stack manager stack-emptied invoked. reason={reason}, shutdown={_isShuttingDown()}, shutdownRequested={_isShutdownRequested()}, transcriptionReady={_isTranscriptionReady()}, hiddenByUser={_hiddenByUser}");
        Startup("stack-emptied", seedHelloOverlay);

        if (!_overlayManager.HasTrackedOverlays())
            Startup("stack-emptied-fallback", seedHelloOverlay);

        QueueDeferredHelloOverlayReseed(reason, helloOverlayKey, seedHelloOverlay);
    }

    public void Reactivate(string reason, string helloOverlayKey, Action seedHelloOverlay)
    {
        _log($"Stack manager reactivation invoked. reason={reason}, shutdown={_isShuttingDown()}, shutdownRequested={_isShutdownRequested()}, transcriptionReady={_isTranscriptionReady()}, hiddenByUser={_hiddenByUser}");
        if (_isShuttingDown() || _isShutdownRequested())
            return;

        if (!_isTranscriptionReady())
            return;

        if (_overlayManager.HasTrackedOverlays())
        {
            _log($"Stack manager reactivation skipped reseed because tracked overlays already exist. reason={reason}");
            return;
        }

        Startup(reason, seedHelloOverlay);
        EnsureHelloOverlay($"reactivation-fallback", helloOverlayKey, seedHelloOverlay);
    }

    public void RestoreAfterReactivation(string reason, string helloOverlayKey, Action seedHelloOverlay)
    {
        if (_isShuttingDown() || !_isTranscriptionReady())
            return;

        ClearHiddenByUser();
        Reactivate(reason, helloOverlayKey, seedHelloOverlay);
        if (!_overlayManager.HasTrackedOverlays())
            Startup($"{reason}-fallback", seedHelloOverlay);

        EnsureHelloOverlay($"{reason}-fallback", helloOverlayKey, seedHelloOverlay);
        QueueDeferredHelloOverlayReseed(reason, helloOverlayKey, seedHelloOverlay);
    }

    public void EnsureHelloOverlay(string reason, string helloOverlayKey, Action seedHelloOverlay)
    {
        if (string.IsNullOrWhiteSpace(helloOverlayKey))
            return;

        if (_overlayManager.HasTrackedOverlay(helloOverlayKey))
            return;

        _log($"Hello overlay reseed fallback ({reason}).");
        Startup("self-heal", seedHelloOverlay);
    }

    public void QueueDeferredHelloOverlayReseed(
        string reason,
        string helloOverlayKey,
        Action seedHelloOverlay)
    {
        if (_isShuttingDown() || _isShutdownRequested())
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(80).ConfigureAwait(false);
                if (_isShuttingDown() || _isShutdownRequested() || !_isTranscriptionReady())
                    return;

                _uiDispatcher.BeginInvoke(new Action(() => EnsureHelloOverlay($"deferred:{reason}", helloOverlayKey, seedHelloOverlay)));
            }
            catch
            {
                // ignore best effort deferred reseed
            }
        });
    }
}
