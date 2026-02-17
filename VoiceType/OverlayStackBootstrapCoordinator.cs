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
        _isHiddenByUser = true;
    }

    public void OnStartup(string reason)
    {
        EnsureHello(reason);
    }

    public void OnStackEmptied(string reason)
    {
        EnsureHello(reason);
    }

    public void OnReactivation(string reason)
    {
        if (_isTranscriptionReady() && !_isShuttingDown() && !_isShutdownRequested())
        {
            if (_isHiddenByUser)
            {
                _isHiddenByUser = false;
                _log($"Stack reactivation ({reason}) cleared user-hidden state.");
                EnsureHello(reason);
                return;
            }

            EnsureHello(reason);
        }
    }

    private void EnsureHello(string reason)
    {
        if (_isShuttingDown() || _isShutdownRequested())
            return;

        if (!_isTranscriptionReady())
            return;

        if (_isHiddenByUser)
        {
            _log($"Stack bootstrap skipped ({reason}) because stack is hidden by user.");
            return;
        }

        _log($"Stack bootstrap ({reason}) reset + reseed hello overlay.");
        _overlayManager.ResetTrackedStack();
        _showHello();
    }
}
