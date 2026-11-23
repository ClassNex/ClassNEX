using Avalonia;
using Avalonia.Threading;
using ClassNex.Controls.NotificationEffects;
using ClassNex.Core.Abstractions;
using ClassNex.Styles;
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

            if (playEffect)
            {
                var center = mainWindow.GetIslandCenterOnScreen() ?? new PixelPoint(0, 0);
                if (mainWindow.Screens.ScreenFromPoint(center) is { } screen)
                    _effectWindow.UpdateWindowPos(screen, 1 / screen.Scaling, false);
                var clipCornerRadius = CiPalette.IslandCornerRadius * mainWindow.ReminderScale;
                _effectWindow.PlayEffect(new RippleEffect(
                    center,
                    CiPalette.AccentBrush(),
                    mainWindow.GetIslandScreenRect,
                    clipCornerRadius));
            }

            mainWindow.ShowNotificationMask(text, leftGlyph, rightGlyph, lifetime);
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

    public void Hide()
    {
        Dispatcher.UIThread.Post(() => { _sound?.Stop(); _window.HideNotificationMask(); });
    }

    public void UpdateCountdown(string text)
    {
        Dispatcher.UIThread.Post(() => Window?.UpdateNotificationOverlayCountdown(text));
    }
}
