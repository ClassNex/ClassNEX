using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Styles;
using System.Globalization;
using Windows.UI.ViewManagement;

namespace ClassNex.Views;

public partial class MainWindow
{
    private DispatcherTimer? _messageTimer;
    private CancellationTokenSource? _turn;
    private bool _onMessage;
    private int _presentation;
    private readonly UISettings _notificationUi = new();
    private bool _hasReminderDetail, _hasCoursePage;

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
        _onMessage = _hasReminderDetail = _hasCoursePage = false;
        _showingNotification = true;
        CardContent.Opacity = 0;
        NotificationMaskText.Text = text;
        NotificationMaskLeftIcon.Text = leftGlyph;
        NotificationMaskRightIcon.Text = rightGlyph;
        NotificationMaskContent.Opacity = 1;
        ArrangeNotification();
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
        NotificationOverlayLeft.Text = left;
        NotificationOverlaySubject.Text = subject;
        NotificationOverlayTeacher.Text = teacher;
        NotificationOverlayTeacher.IsVisible = !string.IsNullOrWhiteSpace(teacher);
        NotificationOverlayTime.Text = time;
        NotificationOverlayTime.IsVisible = !string.IsNullOrWhiteSpace(time);
        NotificationOverlayPrefix.Text = prefix;
        NotificationOverlayPrefix.IsVisible = !string.IsNullOrWhiteSpace(prefix);
        NotificationOverlayMessagePage.Text = message;
        NotificationOverlayLeft.IsVisible = !string.IsNullOrWhiteSpace(left);
        _hasReminderDetail = true;
        _hasCoursePage = NotificationOverlayLeft.IsVisible || !string.IsNullOrWhiteSpace(subject) || NotificationOverlayTime.IsVisible || NotificationOverlayTeacher.IsVisible;
        _onMessage = !_hasCoursePage;
        ResetPage(NotificationOverlayMessagePage, _onMessage);
        ResetPage(NotificationOverlayCoursePage, _hasCoursePage);
        ArrangeNotification();
        NotificationTrackFill.Background = CiPalette.AccentBrush();
        if (!NotificationTrack.IsVisible && trackDuration > TimeSpan.Zero) StartNotificationTrack(trackDuration);
        _ = TurnPageAsync(NotificationMask, NotificationOverlay, verticalPageTurn);
        if (!_hasCoursePage || string.IsNullOrWhiteSpace(message)) return;
        _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _messageTimer.Tick += AdvanceMessage;
        _messageTimer.Start();
    }

    private void ArrangeNotification()
    {
        static FormattedText Measure(TextBlock label, double maxWidth = double.PositiveInfinity)
        {
            var text = new FormattedText(label.Text ?? "", CultureInfo.CurrentUICulture, label.FlowDirection,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize, Brushes.Black);
            if (double.IsFinite(maxWidth)) text.MaxTextWidth = Math.Max(1, maxWidth);
            return text;
        }
        double Natural(TextBlock label) => Math.Ceiling(Measure(label).WidthIncludingTrailingWhitespace);
        var width = Math.Max(360, Math.Min(_normalWidth, 500));
        width = Math.Max(width, Natural(NotificationMaskText) + 84);
        var height = CiPalette.IslandHeight;
        if (_hasReminderDetail)
        {
            var left = NotificationOverlayLeft.IsVisible ? Natural(NotificationOverlayLeft) : 0;
            var prefix = string.IsNullOrWhiteSpace(NotificationOverlayPrefix.Text) ? 0 : Natural(NotificationOverlayPrefix) + 6;
            var subject = Natural(NotificationOverlaySubject);
            var meta = (NotificationOverlayTime.IsVisible ? Natural(NotificationOverlayTime) : 0)
                + (NotificationOverlayTeacher.IsVisible ? Natural(NotificationOverlayTeacher) + 5 : 0);
            NotificationLessonMeta.IsVisible = meta > 0;
            var pairGap = left > 0 ? 20 : 0;
            var inline = left + pairGap + prefix + subject + (meta > 0 ? meta + 6 : 0) + 34;
            var messageWidth = Natural(NotificationOverlayMessagePage) + 48;
            width = Math.Min(AvailableIslandWidth, Math.Max(width, Math.Max(inline, Math.Min(messageWidth, 720))));
            var usable = Math.Max(1, width - 32);
            var leftWidth = Math.Min(left, Math.Floor(usable * 0.42));
            var stacked = inline > width;
            var lessonWidth = Math.Max(1, usable - leftWidth - pairGap);
            NotificationOverlayCoursePage.ColumnSpacing = pairGap;
            NotificationOverlayCoursePage.ColumnDefinitions[0].Width = new GridLength(leftWidth);
            NotificationOverlayCoursePage.ColumnDefinitions[1].Width = GridLength.Star;
            NotificationLesson.ColumnDefinitions[0].Width = stacked ? GridLength.Star : GridLength.Auto;
            NotificationLesson.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
            NotificationLesson.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : GridLength.Auto;
            NotificationLesson.ColumnSpacing = stacked ? 0 : 6;
            NotificationOverlayPrefix.IsVisible = !stacked && !string.IsNullOrWhiteSpace(NotificationOverlayPrefix.Text);
            Grid.SetColumn(NotificationOverlaySubject, stacked ? 0 : 1);
            Grid.SetColumn(NotificationLessonMeta, stacked ? 0 : 2);
            Grid.SetRow(NotificationLessonMeta, stacked ? 1 : 0);
            NotificationLesson.HorizontalAlignment = stacked ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
            NotificationLessonMeta.Orientation = stacked && meta > lessonWidth ? Orientation.Vertical : Orientation.Horizontal;
            NotificationOverlayTeacher.MaxWidth = lessonWidth;
            NotificationOverlayTeacher.TextWrapping = TextWrapping.Wrap;
            NotificationOverlaySubject.MaxWidth = stacked ? lessonWidth : double.PositiveInfinity;
            if (stacked && _hasCoursePage)
            {
                var courseHeight = Measure(NotificationOverlaySubject, lessonWidth).Height;
                if (meta > 0)
                    courseHeight += NotificationLessonMeta.Orientation == Orientation.Vertical
                        ? Measure(NotificationOverlayTeacher, lessonWidth).Height + Measure(NotificationOverlayTime, lessonWidth).Height + 5
                        : Math.Max(Measure(NotificationOverlayTeacher, lessonWidth).Height, Measure(NotificationOverlayTime, lessonWidth).Height);
                height = Math.Max(height, Math.Ceiling(Math.Max(courseHeight, Measure(NotificationOverlayLeft, Math.Max(1, leftWidth)).Height)) + 12);
            }
            if (!string.IsNullOrWhiteSpace(NotificationOverlayMessagePage.Text))
                height = Math.Max(height, Math.Ceiling(Measure(NotificationOverlayMessagePage, width - 48).Height) + 12);
        }
        SetIslandWidth(width);
        NotificationMaskText.MaxWidth = Math.Max(1, IslandBackground.Width - 80);
        height = Math.Max(height, Math.Ceiling(Measure(NotificationMaskText, NotificationMaskText.MaxWidth).Height) + 12);
        height = Math.Min(height, CardContent.MaxHeight);
        LinePanel.Height = IslandBackground.Height = NotificationOverlay.Height = NotificationMask.Height = height;
        NotificationTrack.Width = NotificationTrackFill.Width = Math.Max(1, IslandBackground.Width - 2 * IslandContentMargin);
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
            if (_notificationUi.AnimationsEnabled)
            {
                from.RenderTransform = new TranslateTransform();
                to.RenderTransform = new TranslateTransform();
                await Task.WhenAll(Motion(true).RunAsync(from, turn.Token), Motion(false).RunAsync(to, turn.Token));
            }
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
            var offset = vertical ? 40d : -20d;
            return new Animation
            {
                Duration = TimeSpan.FromMilliseconds(250), Easing = new SplineEasing { X1 = .65, Y1 = 0, X2 = .35, Y2 = 1 },
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, outgoing ? 1d : 0d), new Setter(shift, outgoing ? 0d : -offset) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, outgoing ? 0d : 1d), new Setter(shift, outgoing ? offset : 0d) } }
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
        _hasReminderDetail = _hasCoursePage = _showingNotification = false;
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
