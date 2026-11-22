using ClassNex.Services;
using ClassNex.Core.Abstractions;
using Microsoft.UI.Xaml;

namespace ClassNex.WinUI;

public sealed record OverlaySnapshot(DateTime Now, TodaySummary Today, string CountdownText, bool HasTimetable,
    IReadOnlyDictionary<string, string>? PluginText = null);

public sealed partial class NativeOverlayWindow : Window, INotificationSurface
{
    public bool IsAvailable => _visible;

    public void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect) =>
        OnDispatcher(() => BeginMask(text, leftGlyph, rightGlyph, playEffect));

    public void ShowOverlay(string left, string subject, string teacher, string time, string prefix, string message,
        bool verticalPageTurn, TimeSpan trackDuration) =>
        OnDispatcher(() => BeginAlert(left, subject, teacher, time, prefix, message, verticalPageTurn, trackDuration));

    void INotificationSurface.Hide() => OnDispatcher(EndAlert);

    public void UpdateCountdown(string text) => OnDispatcher(() => UpdateAlertCountdown(text));
}
