namespace VoiceType;

internal enum TranscribedPreviewDecision
{
    TimeoutPaste = 0,
    Cancel = 1,
    PasteWithoutSend = 2
}

internal sealed class TranscribedPreviewCoordinator
{
    private int _activeMessageId;
    private TaskCompletionSource<TranscribedPreviewDecision>? _decisionSource;

    public bool IsActive => _activeMessageId != 0 && _decisionSource != null;

    public Task<TranscribedPreviewDecision> Begin(int messageId)
    {
        if (messageId <= 0)
            throw new ArgumentOutOfRangeException(nameof(messageId), "Message id must be positive.");

        if (IsActive)
            throw new InvalidOperationException("A transcribed preview is already active.");

        _activeMessageId = messageId;
        _decisionSource = new TaskCompletionSource<TranscribedPreviewDecision>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        return _decisionSource.Task;
    }

    public void End()
    {
        _activeMessageId = 0;
        _decisionSource = null;
    }

    public bool TryResolve(TranscribedPreviewDecision decision)
    {
        var source = _decisionSource;
        if (_activeMessageId == 0 || source == null)
            return false;

        return source.TrySetResult(decision);
    }

    public bool TryResolveFromOverlayTap(int messageId)
    {
        if (messageId <= 0 || messageId != _activeMessageId)
            return false;

        return TryResolve(TranscribedPreviewDecision.Cancel);
    }
}
