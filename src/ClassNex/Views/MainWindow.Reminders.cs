using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class MainWindow
{
    private DispatcherTimer? _messageTimer;
    private CancellationTokenSource? _turn;
    private bool _onMessage;
    private int _presentation;

    public PixelRect? GetIslandScreenRect()
    {
        if (!IslandBackground.IsLoaded) return null;
        var start = IslandBackground.PointToScreen(default);
        var end = IslandBackground.PointToScreen(new Point(IslandBackground.Bounds.Width, IslandBackground.Bounds.Height));
        return new(start, new PixelSize(Math.Max(1, end.X - start.X), Math.Max(1, end.Y - start.Y)));
    }

    public PixelPoint? GetIslandCenterOnScreen() => !IslandBackground.IsLoaded ? null :
        IslandBackground.PointToScreen(new Point(IslandBackground.Bounds.Width / 2, IslandBackground.Bounds.Height / 2));

    public void ShowNotificationMask(string text, string leftGlyph = "\uE0FF", string rightGlyph = "\uE54F", TimeSpan lifetime = default)
    {
        if (!CanPresentReminder) return;
        FinishIslandDrag(false);
        ++_presentation;
        StopNotificationOverlayMessageTicker();
        CancelTurn();
        ResetPage(NotificationOverlayMessagePage, false);
        ResetPage(NotificationOverlayCoursePage, true);
        ResetPage(NotificationOverlay, false);
        ResetPage(NotificationMask, true);
        _onMessage = false;
        _showingNotification = true;
        SetIslandWidth(462);
        LinePanel.Height = IslandBackground.Height = CiPalette.IslandHeight;
        CardContent.Opacity = 0;
        NotificationMaskText.MaxWidth = Math.Max(1, IslandBackground.Width - 92);
        NotificationMaskText.Text = text;
        NotificationMaskLeftIcon.Text = leftGlyph;
        NotificationMaskRightIcon.Text = rightGlyph;
        NotificationMaskContent.Opacity = 1;
        NotificationMaskBg.IsOpened = true;
        NotificationTrackFill.Background = CiPalette.OnAccentBrush();
        if (lifetime > TimeSpan.Zero) StartNotificationTrack(lifetime);
        else NotificationTrack.IsVisible = false;
    }

    public void ShowNotificationOverlay(string left, string subject, string teacher, string time, string prefix = "下节课",
        string message = "", bool verticalPageTurn = false, TimeSpan trackDuration = default)
    {
        if (!CanPresentReminder || !_showingNotification) return;
        StopNotificationOverlayMessageTicker();
        _onMessage = false;
        NotificationOverlayLeft.Text = left;
        NotificationOverlaySubject.Text = subject;
        NotificationOverlayTeacher.Text = teacher;
        NotificationOverlayTeacher.IsVisible = !string.IsNullOrWhiteSpace(teacher);
        NotificationOverlayTime.Text = time;
        NotificationOverlayPrefix.Text = prefix;
        NotificationOverlayPrefix.IsVisible = !string.IsNullOrWhiteSpace(prefix);
        NotificationOverlayMessagePage.Text = message;
        ResetPage(NotificationOverlayMessagePage, false);
        ResetPage(NotificationOverlayCoursePage, true);
        NotificationTrackFill.Background = CiPalette.SakuraAccentBrush();
        if (!NotificationTrack.IsVisible && trackDuration > TimeSpan.Zero) StartNotificationTrack(trackDuration);
        _ = TurnPageAsync(NotificationMask, NotificationOverlay, verticalPageTurn);
        if (string.IsNullOrWhiteSpace(message)) return;
        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _messageTimer.Tick += AdvanceMessage;
        _messageTimer.Start();
    }

    private void AdvanceMessage(object? sender, EventArgs e)
    {
        if (!_showingNotification || !CanPresentReminder || _turn is not null) return;
        var from = _onMessage ? (Control)NotificationOverlayMessagePage : NotificationOverlayCoursePage;
        var to = _onMessage ? (Control)NotificationOverlayCoursePage : NotificationOverlayMessagePage;
        _onMessage = !_onMessage;
        _ = TurnPageAsync(from, to, true);
    }

    private async Task TurnPageAsync(Control from, Control to, bool vertical)
    {
        CancelTurn();
        using var turn = new CancellationTokenSource();
        _turn = turn;
        try
        {
            to.IsVisible = true;
            from.IsVisible = true;
            from.Opacity = 1;
            to.Opacity = 0;
            from.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
            to.RenderTransform = new TransformGroup { Children = { new ScaleTransform(), new TranslateTransform() } };
            await Task.WhenAll(Motion(true).RunAsync(from, turn.Token), Motion(false).RunAsync(to, turn.Token));
            if (turn.IsCancellationRequested) return;
            ResetPage(from, false);
            ResetPage(to, true);
            if (from == NotificationMask) NotificationMaskBg.IsOpened = false;
        }
        catch (OperationCanceledException) when (turn.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_turn, turn)) _turn = null; }

        Animation Motion(bool outgoing)
        {
            var shift = vertical ? TranslateTransform.YProperty : TranslateTransform.XProperty;
            var scale = vertical ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;
            var offset = vertical ? 25d : 20d;
            return new Animation
            {
                Duration = TimeSpan.FromMilliseconds(200), Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, outgoing ? 1d : 0d), new Setter(shift, outgoing ? 0d : offset), new Setter(scale, outgoing ? 1d : 0.88d) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, outgoing ? 0d : 1d), new Setter(shift, outgoing ? -offset : 0d), new Setter(scale, outgoing ? 0.88d : 1d) } }
                }
            };
        }
    }

    private static void ResetPage(Control view, bool visible)
    {
        view.IsVisible = visible;
        view.RenderTransform = null;
        view.Opacity = visible ? 1 : 0;
    }

    private void CancelTurn()
    {
        var old = _turn;
        _turn = null;
        old?.Cancel();
    }

    private void StopNotificationOverlayMessageTicker()
    {
        if (_messageTimer is not { } timer) return;
        timer.Stop();
        timer.Tick -= AdvanceMessage;
        _messageTimer = null;
    }

    private void StartNotificationTrack(TimeSpan duration)
    {
        var width = Math.Max(1, IslandBackground.Width - 2 * IslandContentMargin);
        NotificationTrack.Width = NotificationTrackFill.Width = width;
        NotificationTrack.IsVisible = true;
        if (ElementComposition.GetElementVisual(NotificationTrackFill) is not { } visual) return;
        visual.StopAnimation(nameof(visual.Scale));
        visual.CenterPoint = new Vector3D(0, 0, 0);
        visual.Scale = new Vector3D(1, 1, 1);
        var anim = visual.Compositor.CreateVector3DKeyFrameAnimation();
        anim.InsertKeyFrame(0f, new Vector3D(1, 1, 1));
        anim.InsertKeyFrame(1f, new Vector3D(0, 1, 1), new LinearEasing());
        anim.Duration = duration;
        visual.StartAnimation(nameof(visual.Scale), anim);
    }

    public void UpdateNotificationOverlayCountdown(string text)
    {
        if (_showingNotification && NotificationOverlay.IsVisible) NotificationOverlayLeft.Text = text;
    }

    public void HideNotificationMask()
    {
        if (_showingNotification) ResetNotification();
    }

    private void ResetNotification()
    {
        ++_presentation;
        StopNotificationOverlayMessageTicker();
        CancelTurn();
        NotificationMaskBg.IsOpened = false;
        ResetPage(NotificationMask, false);
        ResetPage(NotificationOverlay, false);
        NotificationTrack.IsVisible = false;
        if (ElementComposition.GetElementVisual(NotificationTrackFill) is { } visual) visual.StopAnimation(nameof(visual.Scale));
        _showingNotification = false;
        LinePanel.Height = double.NaN;
        if (_nexPanelActive) return;
        CardContent.Opacity = 1;
        IslandBackground.Height = Math.Max(CiPalette.IslandHeight, CardContent.DesiredSize.Height);
        SetIslandWidth(CardContent.Bounds.Width > 0 ? CardContent.Bounds.Width + 2 * IslandContentMargin : _normalWidth);
    }

    public void WarmNotificationMask()
    {
        if (_showingNotification || !IsVisible) return;
        var generation = _presentation;
        NotificationMaskBg.IsOpened = true;
        DispatcherTimer.RunOnce(() =>
        {
            if (generation == _presentation && !_showingNotification) NotificationMaskBg.IsOpened = false;
        }, TimeSpan.FromMilliseconds(60));
    }
}
