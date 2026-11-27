using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class MainWindow
{
    private CancellationTokenSource? _detailFade;
    private RectangleGeometry? _returnClip;
    private bool _returnSizing;
    private Size _returnTarget;

    private Size ClassroomReturnSize => new(
        Math.Min(AvailableIslandWidth, Math.Ceiling(CardContent.Bounds.Width > 0 ? CardContent.Bounds.Width + 2 * IslandContentMargin : _normalWidth)),
        Math.Max(CiPalette.IslandHeight, Math.Ceiling(CardContent.DesiredSize.Height)));

    private void BeginClassroomReturn()
    {
        _returnClip = new RectangleGeometry();
        UpdateClassroomClip();
        CardContent.Clip = _returnClip;
        LayoutUpdated += TrackClassroomReturn;
        if (!NotificationOverlay.IsVisible) CardContent.Opacity = 1;
    }

    private void UpdateClassroomClip()
    {
        if (_returnClip is not { } clip || IslandBackground.TranslatePoint(default, CardContent) is not { } origin) return;
        var rect = new Rect(origin, IslandBackground.Bounds.Size);
        var r = IslandBackground.CornerRadius.TopLeft;
        if (clip.Rect != rect) clip.Rect = rect;
        if (clip.RadiusX != r) clip.RadiusX = clip.RadiusY = r;
    }

    private async Task FadeDetailToClassroomAsync(int presentation)
    {
        using var fade = new CancellationTokenSource();
        _detailFade = fade;
        try
        {
            static Animation Fade(double from, double to, int duration, Easing easing) => new()
            {
                Duration = TimeSpan.FromMilliseconds(duration), Easing = easing, FillMode = FillMode.Both,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, to) } }
                }
            };
            CardContent.Classes.Remove("reminder-fade");
            CardContent.Opacity = 0;
            bool StillCurrent() => !fade.IsCancellationRequested && presentation == _presentation && _overlayExiting;
            await Fade(NotificationOverlay.Opacity, 0, 125, new SineEaseIn()).RunAsync(NotificationOverlay, fade.Token);
            if (!StillCurrent()) return;
            NotificationOverlay.Opacity = 0;
            await Fade(0, 1, 150, new SineEaseOut()).RunAsync(CardContent, fade.Token);
            if (!StillCurrent()) return;
            CardContent.Opacity = 1;
            ReturnToClassroom();
        }
        catch (OperationCanceledException) when (fade.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_detailFade, fade)) _detailFade = null; }
    }

    private void ReturnToClassroom()
    {
        if (!_overlayExiting) return;
        ClearNotificationPages();
        ApplyNotificationBackdrop(false);
        _returnSizing = true;
        SetClassroomReturnSize(DesktopMotion.IsEnabled);
        TrackClassroomReturn(null, EventArgs.Empty);
    }

    private void SetClassroomReturnSize(bool animate)
    {
        var target = ClassroomReturnSize;
        if (_returnSizing && target == _returnTarget && IslandBackground.Classes.Contains("classroom-return") == animate) return;
        _returnTarget = target;
        IslandBackground.Classes.Set("classroom-return", animate);
        SetIslandWidth(target.Width, reminder: animate);
        IslandBackground.Height = target.Height;
    }

    private void TrackClassroomReturn(object? sender, EventArgs e)
    {
        UpdateClassroomClip();
        if (!_returnSizing) return;
        var size = ClassroomReturnSize;
        if (size != _returnTarget) SetClassroomReturnSize(DesktopMotion.IsEnabled);
        if (Math.Abs(IslandBackground.Bounds.Width - size.Width) > .6 || Math.Abs(IslandBackground.Bounds.Height - size.Height) > .6) return;
        _showingNotification = false;
        UpdateClassroomSurface();
        StopClassroomReturn();
    }

    private void StopClassroomReturn()
    {
        if (_detailFade is { IsCancellationRequested: false } fade) fade.Cancel();
        var visibleSize = _showingNotification && (_returnSizing || IslandBackground.Classes.Contains("reminder-resize"))
            ? IslandBackground.Bounds.Size : default;
        _returnSizing = _overlayExiting = false;
        _returnTarget = default;
        LayoutUpdated -= TrackClassroomReturn;
        CardContent.Clip = null;
        _returnClip = null;
        IslandBackground.Classes.Remove("classroom-return");
        IslandBackground.Classes.Remove("reminder-resize");
        if (_showingNotification && visibleSize.Width > 0 && visibleSize.Height > 0)
        {
            IslandBackground.Width = visibleSize.Width;
            IslandBackground.Height = visibleSize.Height;
        }
    }
}
