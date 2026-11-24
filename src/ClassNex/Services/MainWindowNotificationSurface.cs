using Avalonia.Threading;
using ClassNex.Controls.NotificationEffects;
using ClassNex.Core.Abstractions;
using ClassNex.Views;

namespace ClassNex.Services;

public sealed class MainWindowNotificationSurface : INotificationSurface
{
    private readonly TopmostEffectWindow _effectWindow;
    private readonly MainWindow _window;
    private readonly ReminderSound? _sound;

    public MainWindowNotificationSurface(MainWindow window, TopmostEffectWindow effectWindow) : this(window, effectWindow, null) { }

    internal MainWindowNotificationSurface(MainWindow window, TopmostEffectWindow effectWindow, ReminderSound? sound)
    {
        _window = window;
        _sound = sound;
        _effectWindow = effectWindow;
    }

    private MainWindow? Window => _window.CanPresentReminder ? _window : null;

    public bool IsAvailable => _window.CanPresentReminder;

    public void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect) =>
        ShowMask(text, leftGlyph, rightGlyph, playEffect, TimeSpan.Zero);

    public void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect, TimeSpan lifetime)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var mainWindow = Window;
            if (mainWindow is null)
                return;

            if (playEffect && mainWindow.GetIslandCenterOnScreen() is { } center && mainWindow.Screens.ScreenFromPoint(center) is { } screen)
            {
                _effectWindow.UpdateWindowPos(screen, 1 / screen.Scaling, WindowsOverlay.ForegroundCovers(screen.Bounds));
                _effectWindow.PlayEffect(new RippleEffect(center, mainWindow.ClassroomNotificationAccent));
            }

            mainWindow.ShowNotificationMask(text, leftGlyph, rightGlyph);
            _sound?.Play();
        });
    }

    public void ShowOverlay(
        string left,
        string subject,
        string teacher,
        string time,
        string prefix,
        string message,
        bool verticalPageTurn,
        TimeSpan trackDuration)
    {
        Dispatcher.UIThread.Post(() => Window?.ShowNotificationOverlay(
            left,
            subject,
            teacher,
            time,
            prefix,
            message,
            verticalPageTurn,
            trackDuration));
    }

    public void ShowOverlay(string left, string subject, string teacher, string time, string prefix, string message,
        bool verticalPageTurn, TimeSpan trackDuration, INotificationOverlayProgress progress) =>
        ShowOverlay(new(left, subject, teacher, time, prefix, message, verticalPageTurn), null, trackDuration, progress);

    public void ShowOverlay(NotificationOverlay detail, int? seconds, TimeSpan trackDuration, INotificationOverlayProgress progress) =>
        Dispatcher.UIThread.Post(() => Window?.ShowNotificationOverlay(detail, seconds, progress));

    public void UpdateCountdown(int seconds) => Dispatcher.UIThread.Post(() => Window?.UpdateNotificationOverlayCountdown(seconds));

    public void Hide()
    {
        Dispatcher.UIThread.Post(() => { _sound?.Stop(); _window.HideNotificationMask(); });
    }

    public void UpdateCountdown(string text)
    {
        Dispatcher.UIThread.Post(() => Window?.UpdateNotificationOverlayCountdown(text));
    }
}
