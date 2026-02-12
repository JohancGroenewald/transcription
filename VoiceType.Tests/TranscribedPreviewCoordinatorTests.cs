namespace VoiceType.Tests;

public class TranscribedPreviewCoordinatorTests
{
    [Fact]
    public async Task Begin_Throws_WhenMessageIdIsNotPositive()
    {
        var coordinator = new TranscribedPreviewCoordinator();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coordinator.Begin(0));
    }

    [Fact]
    public async Task Begin_Throws_WhenAlreadyActive()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        _ = coordinator.Begin(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.Begin(2));
    }

    [Fact]
    public async Task TryResolve_ReturnsDecision_WhenActive()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        var task = coordinator.Begin(41);

        var resolved = coordinator.TryResolve(TranscribedPreviewDecision.PasteWithoutSend);

        Assert.True(resolved);
        Assert.Equal(TranscribedPreviewDecision.PasteWithoutSend, await task);
    }

    [Fact]
    public void TryResolve_ReturnsFalse_WhenNotActive()
    {
        var coordinator = new TranscribedPreviewCoordinator();

        var resolved = coordinator.TryResolve(TranscribedPreviewDecision.Cancel);

        Assert.False(resolved);
    }

    [Fact]
    public void TryResolveFromOverlayTap_ReturnsFalse_WhenMessageDoesNotMatch()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        _ = coordinator.Begin(7);

        var resolved = coordinator.TryResolveFromOverlayTap(8);

        Assert.False(resolved);
    }

    [Fact]
    public async Task TryResolveFromOverlayTap_ResolvesCancel_WhenMessageMatches()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        var task = coordinator.Begin(9);

        var resolved = coordinator.TryResolveFromOverlayTap(9);

        Assert.True(resolved);
        Assert.Equal(TranscribedPreviewDecision.Cancel, await task);
    }

    [Fact]
    public void End_ClearsActiveState()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        _ = coordinator.Begin(3);

        coordinator.End();

        Assert.False(coordinator.IsActive);
        Assert.False(coordinator.TryResolve(TranscribedPreviewDecision.Cancel));
    }

    [Fact]
    public void End_AllowsStartingNewSession()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        _ = coordinator.Begin(5);

        coordinator.End();
        var next = coordinator.Begin(6);

        Assert.True(coordinator.IsActive);
        Assert.False(next.IsCompleted);
    }

    [Fact]
    public void TryResolveFromOverlayTap_ReturnsFalse_AfterEnd()
    {
        var coordinator = new TranscribedPreviewCoordinator();
        _ = coordinator.Begin(12);
        coordinator.End();

        var resolved = coordinator.TryResolveFromOverlayTap(12);

        Assert.False(resolved);
    }
}
