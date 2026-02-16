using System.Drawing;
using System.Windows.Forms;

namespace VoiceType;

public interface IOverlayManager : IDisposable
{
    event EventHandler<int>? OverlayTapped;

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
        bool autoHide = true,
        bool animateHide = false);

    void ApplyHudSettings(
        int opacityPercent,
        int widthPercent,
        int fontSizePt,
        bool showBorder);

    void ApplyFadeProfile(int overlayFadeProfile);

    void HideAll();

    void FadeVisibleOverlaysTopToBottom(int delayBetweenMs = 140);
}
