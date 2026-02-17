using System;

namespace VoiceType;

internal sealed class OverlayStackBootstrapCoordinator
{
    private readonly IOverlayManager _overlayManager;
    private readonly Func<bool> _isShuttingDown;
    private readonly Func<bool> _isShutdownRequested;
    private readonly Func<bool> _isTranscriptionReady;
    private readonly Action<string> _log;
    private readonly Action _showHello;
    private bool _isHiddenByUser;

    public OverlayStackBootstrapCoordinator(
        IOverlayManager overlayManager,
        Func<bool> isShuttingDown,
        Func<bool> isShutdownRequested,
        Func<bool> isTranscriptionReady,
        Action<string> log,
        Action showHello)
    {
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _isShuttingDown = isShuttingDown ?? throw new ArgumentNullException(nameof(isShuttingDown));
        _isShutdownRequested = isShutdownRequested ?? throw new ArgumentNullException(nameof(isShutdownRequested));
        _isTranscriptionReady = isTranscriptionReady ?? throw new ArgumentNullException(nameof(isTranscriptionReady));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _showHello = showHello ?? throw new ArgumentNullException(nameof(showHello));
    }

    public bool IsHiddenByUser => _isHiddenByUser;

    public void MarkHiddenByUser()
    {
        if (_isHiddenByUser)
        {
            _log("Stack hidden-by-user flag already true.");
            return;
        }

        _isHiddenByUser = true;
        _log("Stack hidden-by-user flag set.");
    }

    public void ClearHiddenByUser()
    {
        if (!_isHiddenByUser)
        {
            _log("Stack hidden-by-user flag already false.");
            return;
        }

        _isHiddenByUser = false;
        _log("Stack hidden-by-user flag cleared.");
    }

    public void OnStartup(string reason)
    {
        _log($"Stack bootstrap startup invoked. reason={reason}, shutdown={_isShuttingDown()}, shutdownRequested={_isShutdownRequested()}, transcriptionReady={_isTranscriptionReady()}, hiddenByUser={_isHiddenByUser}");
        EnsureHello(reason);
    }

    public void OnStackEmptied(string reason)
    {
        _log($"Stack bootstrap stack-emptied invoked. reason={reason}, shutdown={_isShuttingDown()}, shutdownRequested={_isShutdownRequested()}, transcriptionReady={_isTranscriptionReady()}, hiddenByUser={_isHiddenByUser}");
        EnsureHello(reason);
    }

    public void OnReactivation(string reason)
    {
        _log($"Stack bootstrap reactivation invoked. reason={reason}, shutdown={_isShuttingDown()}, shutdownRequested={_isShutdownRequested()}, transcriptionReady={_isTranscriptionReady()}, hiddenByUser={_isHiddenByUser}");
        if (_isTranscriptionReady() && !_isShuttingDown() && !_isShutdownRequested())
        {
            if (_overlayManager.HasTrackedOverlays())
            {
                _log($"Stack bootstrap reactivation skipped reseed because tracked overlays already exist. reason={reason}");
                return;
            }

            EnsureHello(reason);
        }
    }

    private void EnsureHello(string reason)
    {
        if (_isShuttingDown() || _isShutdownRequested())
        {
            _log($"Stack bootstrap ignore ({reason}) because app is shutting down.");
            return;
        }

        if (!_isTranscriptionReady())
        {
            _log($"Stack bootstrap ignore ({reason}) because transcription service is not ready.");
            return;
        }

        _log($"Stack bootstrap ({reason}) reset + reseed hello overlay.");
        _overlayManager.ResetTrackedStack();
        _showHello();
    }
}
