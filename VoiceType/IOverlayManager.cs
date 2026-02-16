using System.Drawing;
using System.Windows.Forms;

namespace VoiceType;

public interface IOverlayManager : IDisposable
{
    event EventHandler<int>? OverlayTapped;
    event EventHandler<OverlayCopyTappedEventArgs>? OverlayCopyTapped;

    int ShowMessage(
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
        bool animateHide = false,
        bool showListeningLevelMeter = false,
        int listeningLevelPercent = 0,
        string? copyText = null,
        bool isSubmittedAction = false,
        bool fullWidthText = false);

    void ApplyHudSettings(
        int opacityPercent,
        int widthPercent,
        int fontSizePt,
        bool showBorder);

    void ApplyFadeProfile(int overlayFadeProfile);

    void HideAll();

    void FadeVisibleOverlaysTopToBottom(int delayBetweenMs = 140);

    void DismissRemoteActionOverlays();

    void DismissSubmittedActionOverlays(int keepGlobalMessageId = 0);

    void DismissCopyActionOverlays(int keepGlobalMessageId = 0);

    void ClearCountdownBar(string overlayKey);

    int GetStackHorizontalOffset();

    void SetStackHorizontalOffset(int offsetPx);

    void HideOverlays(IEnumerable<string> overlayKeys);

    void HideOverlay(string overlayKey);
}
