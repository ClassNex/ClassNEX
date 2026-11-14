using ClassNex.Models;

namespace ClassNex.Core.Abstractions;

public interface INotificationService
{
    void Start();

    void Stop();

    void DebugTriggerClassPreparing();

    void DebugTriggerClassOn();

    void DebugTriggerClassOff();

    void DebugTriggerTestNotification();
}

public interface INotificationSurface
{
    bool IsAvailable { get; }

    void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect);

    void ShowOverlay(
        string left,
        string subject,
        string teacher,
        string time,
        string prefix,
        string message,
        bool verticalPageTurn,
        TimeSpan trackDuration);

    void Hide();

    void UpdateCountdown(string text);
}
