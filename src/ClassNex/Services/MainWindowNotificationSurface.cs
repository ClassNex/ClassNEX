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

    public MainWindowNotificationSurface(TopmostEffectWindow effectWindow)
    {
        _effectWindow = effectWindow;
    }

    private static MainWindow? Window =>
        AppServices.Settings.IsMainWindowVisible &&
        AppServices.MainWindow is { IsEditMode: false } mainWindow
            ? mainWindow
            : null;

    public bool IsAvailable => Window is not null;

    public void ShowMask(string text, string leftGlyph, string rightGlyph, bool playEffect)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var mainWindow = Window;
            if (mainWindow is null)
                return;

            if (playEffect)
            {
                var center = mainWindow.GetIslandCenterOnScreen() ?? new PixelPoint(0, 0);
                var clipCornerRadius = 14 * Math.Max(0.1, AppServices.Settings.EffectiveScale);
                _effectWindow.PlayEffect(new RippleEffect(
                    center,
                    CiPalette.SakuraAccentBrush(),
                    mainWindow.GetIslandScreenRect,
                    clipCornerRadius));
            }

            mainWindow.ShowNotificationMask(text, leftGlyph, rightGlyph);
        });
    }

    public void ShowOverlay(
        string left,
        string subject,
        string teacher,
        string time,
        string prefix,
        string message,
        bool verticalPageTurn)
    {
        Dispatcher.UIThread.Post(() => Window?.ShowNotificationOverlay(
            left,
            subject,
            teacher,
            time,
            prefix,
            message,
            verticalPageTurn));
    }

    public void Hide()
    {
        Dispatcher.UIThread.Post(() => Window?.HideNotificationMask());
    }

    public void UpdateCountdown(string text)
    {
        Dispatcher.UIThread.Post(() => Window?.UpdateNotificationOverlayCountdown(text));
    }
}
