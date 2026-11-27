using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Core.Abstractions;
using ClassNex.Services;
using ClassNex.Styles;
using System.Diagnostics;
using System.Globalization;

namespace ClassNex.Views;

public partial class MainWindow
{
    private static readonly TimeSpan PageInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RailInterval = TimeSpan.FromMilliseconds(50);
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Render);
    private long? _pageStartedAt;
    private INotificationOverlayProgress? _overlayProgress;
    private readonly Transitions? _hoverTransitions, _railTransitions, _maskShadowTransitions;
#if DEBUG
    internal TimeSpan ReviewOverlayDuration { get; set; }
#endif
    private bool _overlayExiting;
    private bool _staticSlides;
    public static readonly StyledProperty<double> CountdownProgressValueProperty =
        AvaloniaProperty.Register<MainWindow, double>(nameof(CountdownProgressValue), 1.0);

    public double CountdownProgressValue
    {
        get => GetValue(CountdownProgressValueProperty);
        set => SetValue(CountdownProgressValueProperty, value);
    }

    private const double NotificationHeight = 40;

    private void ApplyMotionPreference()
    {
        if (_closed) return;
        var animate = DesktopMotion.IsEnabled;
        HoverHost.Transitions = animate && !_readingOverflow ? _hoverTransitions : null;
        OverlayTimeProgressBar.Transitions = animate ? _railTransitions : null;
        NotificationMaskShadow.Transitions = animate ? _maskShadowTransitions : null;
    }

    private void OnMotionPreferenceChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        ApplyMotionPreference();
        if (DesktopMotion.IsEnabled) return;
        ClearMaskAnimationClasses();
        NotificationMaskContent.Opacity = 1;
        if (NotificationMaskContent.RenderTransform is TransformGroup { Children: [ScaleTransform titleScale] })
            titleScale.ScaleX = titleScale.ScaleY = 1;
        NotificationMaskShadow.Opacity = 0;
        if (!_showingNotification)
        {
            NotificationMaskBg.Settle(false);
            return;
        }
        if (_overlayExiting)
        {
            ResetNotification();
            return;
        }
        CardContent.Classes.Remove("reminder-fade");
        IslandBackground.Classes.Remove("reminder-resize");
        SetStaticSlides(true);
        if (!NotificationMask.IsVisible) return;
        if (NotificationOverlay.IsVisible) ResetPage(NotificationMask, false);
        NotificationMaskBg.Settle(!NotificationOverlay.IsVisible);
    });

    private void ClearMaskAnimationClasses()
    {
        NotificationMaskContent.Classes.Remove("entering");
        NotificationMaskContent.Classes.Remove("revealing");
        NotificationMaskShadow.Classes.Remove("entering");
        NotificationMaskShadow.Classes.Remove("revealing");
    }

    private void SetStaticSlides(bool stationary)
    {
        if (_staticSlides == stationary) return;
        var theme = stationary ? (ControlTheme)NotificationSlides.FindResource("StationaryReminderSlide")! : NotificationSlides.ItemContainerTheme;
        foreach (var item in NotificationSlides.Items.OfType<ListBoxItem>()) item.Theme = theme;
        _staticSlides = stationary;
    }

    private static readonly FontFamily NotificationLucide = new("avares://ClassNex/Assets/NotificationFonts/#lucide");
    private static readonly FontFamily NotificationFluent = new("avares://ClassNex/Assets/NotificationFonts/#FluentSystemIcons-Resizable");
    private static readonly FontFamily ApplicationLucide = new("avares://ClassNex/Assets/Fonts/#lucide");
    private int _presentation;
    private bool _hasCoursePage;
    private PixelPoint? _lastNotificationCenter;
    internal IBrush? ClassroomNotificationAccent => NotificationMaskBg.Fill;

    public PixelRect? GetIslandScreenRect()
    {
        if (!IslandBackground.IsLoaded) return null;
        var start = IslandBackground.PointToScreen(default);
        var end = IslandBackground.PointToScreen(new Point(IslandBackground.Bounds.Width, IslandBackground.Bounds.Height));
        return new(start, new PixelSize(Math.Max(1, end.X - start.X), Math.Max(1, end.Y - start.Y)));
    }

    public PixelPoint? GetIslandCenterOnScreen()
    {
        if (!IslandBackground.IsLoaded || IslandBackground.Bounds.Width <= 0 || IslandBackground.Bounds.Height <= 0) return _lastNotificationCenter;
        return _lastNotificationCenter = IslandBackground.PointToScreen(new Point(IslandBackground.Bounds.Width / 2, IslandBackground.Bounds.Height / 2));
    }

    private static FontFamily IconFont(string glyph) => glyph switch
    {
        "\uED34" => NotificationFluent,
        "\uE0FF" or "\uE54F" or "\uE250" or "\uE224" => NotificationLucide,
        _ => ApplicationLucide
    };

    public void ShowNotificationMask(string text, string leftGlyph = "\uE0FF", string rightGlyph = "\uE54F", TimeSpan lifetime = default)
    {
        if (!CanPresentReminder) return;
        FinishIslandDrag(false);
        ++_presentation;
        StopClassroomReturn();
        StopOverlayProgress();
        _overlayExiting = _hasCoursePage = false;
        CardContent.Classes.Set("reminder-fade", DesktopMotion.IsEnabled);
        SetStaticSlides(!DesktopMotion.IsEnabled);
        MaskTitleViewport.Offset = NotificationBodyViewport.Offset = default;
        ClearMaskAnimationClasses();
        NotificationSlides.SelectedIndex = 0;
        ResetPage(NotificationOverlay, false);
        CountdownProgressValue = 1.0;
        ResetPage(NotificationMask, true);
        _showingNotification = true;
        CardContent.Opacity = 1;
        NotificationMaskText.Text = text;
        NotificationMaskLeftIcon.FontFamily = IconFont(leftGlyph);
        NotificationMaskRightIcon.FontFamily = IconFont(rightGlyph);
        NotificationMaskLeftIcon.Glyph = leftGlyph;
        NotificationMaskRightIcon.Glyph = rightGlyph;
        var breakMask = rightGlyph == "\uE250";
        NotificationMaskIcons.Margin = breakMask ? new Thickness(16, 0) : default;
        NotificationMaskLeftIcon.Margin = NotificationMaskRightIcon.Margin = breakMask ? default : new Thickness(0, -1.5, 0, -1.5);
        NotificationMaskLeftIcon.FontWeight = NotificationMaskRightIcon.FontWeight = breakMask ? FontWeight.Medium : FontWeight.Normal;
        ApplyNotificationBackdrop(true);
        ArrangeNotification();
        if (DesktopMotion.IsEnabled)
        {
            NotificationMaskContent.Classes.Add("entering");
            NotificationMaskShadow.Classes.Add("entering");
            if (NotificationMaskBg.IsOpened) NotificationMaskBg.Open(); else NotificationMaskBg.IsOpened = true;
        }
        else NotificationMaskBg.Settle(true);
    }

    public void ShowNotificationOverlay(string left, string subject, string teacher, string time, string prefix = "下节课",
        string message = "", bool verticalPageTurn = false, TimeSpan trackDuration = default) =>
        ShowNotificationOverlay(new(left, subject, teacher, time, prefix, message, verticalPageTurn), null,
            trackDuration > TimeSpan.Zero ? new NotificationOverlayPlayback(trackDuration) : null);

    internal void ShowNotificationOverlay(NotificationOverlay detail, int? seconds, INotificationOverlayProgress? progress)
    {
        if (!CanPresentReminder || !_showingNotification || _overlayExiting) return;
        StopOverlayProgress();
        NotificationOverlayLeft.IsVisible = seconds.HasValue;
        NotificationBreakCaption.IsVisible = !seconds.HasValue && detail.Break.HasValue;
        NotificationPlainCaption.IsVisible = !seconds.HasValue && !detail.Break.HasValue;
        if (seconds is { } count) NotificationSeconds.Text = count.ToString(CultureInfo.CurrentCulture);
        else if (detail.Break is { } pause)
        {
            NotificationBreakName.Text = pause.Name;
            NotificationBreakDuration.Text = pause.Duration;
        }
        else NotificationPlainCaption.Text = detail.Caption;
        NotificationOverlaySubject.Text = detail.Subject;
        NotificationOverlayTeacher.Text = detail.Teacher;
        NotificationOverlayTeacher.IsVisible = !string.IsNullOrWhiteSpace(detail.Teacher);
        NotificationOverlayTime.Text = detail.Time.Replace('–', '-');
        NotificationOverlayPrefix.Text = detail.Prefix == "下节课是" ? "下节课是：" : detail.Prefix;
        NotificationOverlayMessagePage.Text = NotificationSimpleMessage.Text = detail.Message;
        _hasCoursePage = seconds.HasValue || detail.Break.HasValue || !string.IsNullOrEmpty(detail.Caption) ||
                         !string.IsNullOrEmpty(detail.Subject) || !string.IsNullOrEmpty(detail.Time) || !string.IsNullOrEmpty(detail.Teacher);
        NotificationCourseProvider.IsVisible = _hasCoursePage;
        NotificationSimpleProvider.IsVisible = !_hasCoursePage;
        NotificationSlides.SelectedIndex = 0;
        CardContent.Opacity = 0;
        ResetPage(NotificationOverlay, true);
        ArrangeNotification();
        _overlayProgress = progress;
        ProgressContainer.IsVisible = progress is not null;
        if (progress is not null) CountdownProgressValue = progress.LeftProgress;
        if (_hasCoursePage && detail.Vertical && !string.IsNullOrWhiteSpace(detail.Message))
            _pageStartedAt = Stopwatch.GetTimestamp();
        if (progress is not null || _pageStartedAt.HasValue)
        {
            _progressTimer.Interval = progress is not null ? RailInterval : PageInterval;
            _progressTimer.Start();
        }
        _ = RetreatMaskAsync(_presentation, finish: false);
    }

    private void ApplyNotificationBackdrop(bool active)
    {
        UpdateClassroomSurface();
        IslandSheen.IsVisible = !active;
        var reminderRadius = new CornerRadius(ClassroomReminderRadius);
        NotificationMask.CornerRadius = NotificationMaskShadow.CornerRadius = NotificationOverlay.CornerRadius = reminderRadius;
        IslandBackground.CornerRadius = active ? reminderRadius : new CornerRadius(ClassroomCornerRadius);
        IslandBackground.BorderThickness = active ? new Thickness(1) : default;
        IslandBackground.BoxShadow = active ? BoxShadows.Parse("0 4 8 2 #48000000") : CiPalette.IslandShadow();
        IslandBackground.Background = active && this.TryFindResource("SolidBackgroundFillColorSecondaryBrush", ActualThemeVariant, out var fill) && fill is IBrush brush
            ? brush : CiPalette.IslandSurfaceBrush();
        IslandBackground.BorderBrush = active && this.TryFindResource("ControlElevationBorderBrush", ActualThemeVariant, out var border) ? border as IBrush : null;
    }

    private async Task RetreatMaskAsync(int presentation, bool finish)
    {
        NotificationMaskContent.Classes.Remove("entering");
        NotificationMaskShadow.Classes.Remove("entering");
        if (DesktopMotion.IsEnabled)
        {
            NotificationMaskBg.IsOpened = false;
            NotificationMaskContent.Classes.Add("revealing");
            NotificationMaskShadow.Classes.Add("revealing");
            await NotificationMaskBg.TransitionCompleted;
        }
        else NotificationMaskBg.Settle(false);
        if (presentation != _presentation) return;
        if (finish) ReturnToClassroom();
        else
        {
            ResetPage(NotificationMask, false);
            if (_shrinkAfterMask && !_overlayExiting) ArrangeNotification();
            else RefreshReminderInput();
        }
    }

    private static void ResetPage(Control view, bool visible)
    {
        view.IsVisible = visible;
        view.Opacity = visible ? 1 : 0;
    }

    private void SampleOverlayProgress(object? sender, EventArgs args)
    {
        if (_overlayProgress is { } progress) CountdownProgressValue = progress.LeftProgress;
        if (_pageStartedAt is not { } start) return;
        var page = (int)((Stopwatch.GetElapsedTime(start).Ticks / PageInterval.Ticks) % 2);
        if (NotificationSlides.SelectedIndex == page) return;
        SetStaticSlides(!DesktopMotion.IsEnabled);
        NotificationSlides.SelectedIndex = page;
        NotificationBodyViewport.Offset = default;
        if (_oversizeSlidePair) ArrangeNotification();
    }

    private void StopOverlayProgress()
    {
        _progressTimer.Stop();
        _overlayProgress = null;
        _pageStartedAt = null;
    }

    public void UpdateNotificationOverlayCountdown(string text)
    {
        if (!_showingNotification || !NotificationOverlay.IsVisible) return;
        NotificationOverlayLeft.IsVisible = NotificationBreakCaption.IsVisible = false;
        NotificationPlainCaption.IsVisible = true;
        NotificationPlainCaption.Text = text;
        if (!_overlayExiting) ArrangeNotification();
    }

    internal void UpdateNotificationOverlayCountdown(int seconds)
    {
        if (!_showingNotification || !NotificationOverlay.IsVisible || !NotificationOverlayLeft.IsVisible) return;
        var value = seconds.ToString(CultureInfo.CurrentCulture);
        var previous = NotificationSeconds.Text;
        if (value == previous) return;
        NotificationSeconds.Text = value;
        if (value.Length != previous?.Length && !_overlayExiting) ArrangeNotification();
    }

    public void HideNotificationMask()
    {
        if (!_showingNotification) return;
        if (!CanPresentReminder || !DesktopMotion.IsEnabled)
        {
            ResetNotification();
            return;
        }
        if (_overlayExiting) return;
        if (_overlayProgress is { } progress) CountdownProgressValue = progress.LeftProgress;
        StopOverlayProgress();
        _overlayExiting = true;
        RefreshReminderInput();
        BeginClassroomReturn();
        if (!NotificationOverlay.IsVisible)
        {
            _ = RetreatMaskAsync(_presentation, finish: true);
            return;
        }
        _ = FadeDetailToClassroomAsync(_presentation);
    }

    private void ResetNotification()
    {
        ++_presentation;
        StopClassroomReturn();
        StopOverlayProgress();
        ClearNotificationPages();
        _showingNotification = false;
        ApplyNotificationBackdrop(false);
        CardContent.Classes.Remove("reminder-fade");
        if (_nexPanelActive) return;
        CardContent.Opacity = 1;
        SetClassroomReturnSize(animate: false);
    }

    private void ClearNotificationPages()
    {
        ClearMaskAnimationClasses();
        NotificationMaskBg.Settle(false);
        ResetPage(NotificationMask, false);
        ResetPage(NotificationOverlay, false);
        _hasCoursePage = _shrinkAfterMask = _oversizeSlidePair = false;
        RefreshReminderInput();
    }

    public void WarmNotificationMask()
    {
        if (_showingNotification || !IsVisible || !DesktopMotion.IsEnabled) return;
        var generation = _presentation;
        NotificationMaskBg.IsOpened = true;
        DispatcherTimer.RunOnce(() =>
        {
            if (generation == _presentation && !_showingNotification) NotificationMaskBg.IsOpened = false;
        }, TimeSpan.FromMilliseconds(60));
    }
}
