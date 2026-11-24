using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ClassNex.Core.Abstractions;
using ClassNex.Services;
using ClassNex.Styles;
using System.Globalization;
using Windows.UI.ViewManagement;

namespace ClassNex.Views;

public partial class MainWindow
{
    private DispatcherTimer? _messageTimer;
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    private INotificationOverlayProgress? _overlayProgress;
    private bool _overlayExiting;
    public static readonly StyledProperty<double> CountdownProgressValueProperty =
        AvaloniaProperty.Register<MainWindow, double>(nameof(CountdownProgressValue), 1.0);

    public double CountdownProgressValue
    {
        get => GetValue(CountdownProgressValueProperty);
        set => SetValue(CountdownProgressValueProperty, value);
    }

    private const double NotificationHeight = 40;
    private static readonly FontFamily NotificationLucide = new("avares://ClassNex/Assets/NotificationFonts/#lucide");
    private static readonly FontFamily NotificationFluent = new("avares://ClassNex/Assets/NotificationFonts/#FluentSystemIcons-Resizable");
    private static readonly FontFamily ApplicationLucide = new("avares://ClassNex/Assets/Fonts/#lucide");
    private readonly UISettings _notificationUi = new();
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
        StopOverlayProgress();
        StopNotificationOverlayMessageTicker();
        _overlayExiting = _hasCoursePage = false;
        NotificationOverlay.Classes.Remove("leaving");
        NotificationMaskContent.Classes.Remove("revealing");
        NotificationMaskContent.Classes.Remove("entering");
        NotificationMaskShadow.Classes.Remove("entering");
        NotificationMaskShadow.Classes.Remove("revealing");
        NotificationSlides.SelectedIndex = 0;
        ResetPage(NotificationOverlay, false);
        CountdownProgressValue = 1.0;
        ResetPage(NotificationMask, true);
        _showingNotification = true;
        CardContent.Opacity = 0;
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
        if (_notificationUi.AnimationsEnabled)
        {
            NotificationMaskContent.Classes.Add("entering");
            NotificationMaskShadow.Classes.Add("entering");
        }
        if (NotificationMaskBg.IsOpened) NotificationMaskBg.Open(); else NotificationMaskBg.IsOpened = true;
    }

    public void ShowNotificationOverlay(string left, string subject, string teacher, string time, string prefix = "下节课",
        string message = "", bool verticalPageTurn = false, TimeSpan trackDuration = default) =>
        ShowNotificationOverlay(new(left, subject, teacher, time, prefix, message, verticalPageTurn), null,
            trackDuration > TimeSpan.Zero ? new NotificationOverlayPlayback(trackDuration) : null);

    internal void ShowNotificationOverlay(NotificationOverlay detail, int? seconds, INotificationOverlayProgress? progress)
    {
        if (!CanPresentReminder || !_showingNotification) return;
        StopNotificationOverlayMessageTicker();
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
        ResetPage(NotificationOverlay, true);
        ArrangeNotification();
        _overlayProgress = progress;
        ProgressContainer.IsVisible = progress is not null;
        if (progress is not null)
        {
            CountdownProgressValue = 1.0;
            _progressTimer.Start();
        }
        _ = RetreatMaskAsync(_presentation, finish: false);
        if (_hasCoursePage && detail.Vertical && !string.IsNullOrWhiteSpace(detail.Message))
        {
            _messageTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _messageTimer.Tick += AdvanceMessage;
            _messageTimer.Start();
        }
    }

    private void ApplyNotificationBackdrop(bool active)
    {
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

    private void ArrangeNotification()
    {
        var height = NotificationHeight;
        NotificationMaskIcons.Measure(Size.Infinity);
        var width = Math.Max(_normalWidth, NotificationMaskIcons.DesiredSize.Width + 24);
        if (NotificationOverlay.IsVisible && _hasCoursePage)
        {
            NotificationOverlayCoursePage.ColumnDefinitions[0].Width = GridLength.Star;
            NotificationOverlayCoursePage.ColumnDefinitions[1].Width = GridLength.Star;
            NotificationOverlaySubject.MaxWidth = double.PositiveInfinity;
            NotificationOverlaySubject.TextWrapping = TextWrapping.NoWrap;
            var caption = NotificationOverlayLeft.IsVisible ? NotificationOverlayLeft : NotificationBreakCaption.IsVisible ? NotificationBreakCaption : NotificationPlainCaption;
            caption.Measure(Size.Infinity);
            NotificationLesson.Measure(Size.Infinity);
            NotificationOverlayMessagePage.Measure(Size.Infinity);
            var left = caption.DesiredSize.Width;
            var right = NotificationLesson.DesiredSize.Width;
            NotificationOverlayCoursePage.Measure(Size.Infinity);
            width = Math.Max(width, 32 + Math.Max(NotificationOverlayCoursePage.DesiredSize.Width, NotificationOverlayMessagePage.DesiredSize.Width));
            var available = Math.Max(1, AvailableIslandWidth - 32);
            if (Math.Max(left, right) * 2 > available)
            {
                var labelWidth = Math.Min(left, available / 2);
                NotificationOverlayCoursePage.ColumnDefinitions[0].Width = new GridLength(labelWidth);
                width = Math.Max(width, 32 + left + right);
                NotificationOverlaySubject.Measure(Size.Infinity);
                var meta = right - NotificationOverlaySubject.DesiredSize.Width;
                NotificationOverlaySubject.MaxWidth = Math.Max(1, available - labelWidth - meta);
                NotificationOverlaySubject.TextWrapping = TextWrapping.Wrap;
                NotificationLesson.Measure(Size.Infinity);
                height = Math.Max(height, NotificationLesson.DesiredSize.Height);
            }
        }
        else if (NotificationOverlay.IsVisible)
        {
            NotificationSimpleMessage.Measure(Size.Infinity);
            width = Math.Max(width, NotificationSimpleMessage.DesiredSize.Width);
        }
        NotificationDetailLayout.HorizontalAlignment = (AppServices.Settings.ImportedDock?.Anchor % 3) switch
        {
            0 => Avalonia.Layout.HorizontalAlignment.Left,
            2 => Avalonia.Layout.HorizontalAlignment.Right,
            _ => Avalonia.Layout.HorizontalAlignment.Center
        };
        SetIslandWidth(width, reminder: true);
        height = Math.Min(Math.Ceiling(height), CardContent.MaxHeight);
        LinePanel.Height = IslandBackground.Height = NotificationOverlay.Height = NotificationMask.Height = NotificationMaskShadow.Height = height;
    }

    private async Task RetreatMaskAsync(int presentation, bool finish)
    {
        NotificationMaskContent.Classes.Remove("entering");
        NotificationMaskShadow.Classes.Remove("entering");
        NotificationMaskBg.IsOpened = false;
        if (_notificationUi.AnimationsEnabled)
        {
            NotificationMaskContent.Classes.Add("revealing");
            NotificationMaskShadow.Classes.Add("revealing");
            await NotificationMaskBg.TransitionCompleted;
        }
        if (presentation != _presentation) return;
        if (finish) ResetNotification(); else ResetPage(NotificationMask, false);
    }

    private void AdvanceMessage(object? sender, EventArgs e) => NotificationSlides.SelectedIndex = NotificationSlides.SelectedIndex == 0 ? 1 : 0;

    private static void ResetPage(Control view, bool visible)
    {
        view.IsVisible = visible;
        view.Opacity = visible ? 1 : 0;
    }

    private void StopNotificationOverlayMessageTicker()
    {
        if (_messageTimer is not { } timer) return;
        timer.Stop();
        timer.Tick -= AdvanceMessage;
    }

    private void SampleOverlayProgress(object? sender, EventArgs args)
    {
        if (_overlayProgress is { } progress) CountdownProgressValue = progress.LeftProgress;
    }

    private void StopOverlayProgress()
    {
        _progressTimer.Stop();
        _overlayProgress = null;
    }

    public void UpdateNotificationOverlayCountdown(string text)
    {
        if (!_showingNotification || !NotificationOverlay.IsVisible) return;
        NotificationOverlayLeft.IsVisible = NotificationBreakCaption.IsVisible = false;
        NotificationPlainCaption.IsVisible = true;
        NotificationPlainCaption.Text = text;
    }

    internal void UpdateNotificationOverlayCountdown(int seconds)
    {
        if (_showingNotification && NotificationOverlay.IsVisible && NotificationOverlayLeft.IsVisible)
            NotificationSeconds.Text = seconds.ToString(CultureInfo.CurrentCulture);
    }

    public void HideNotificationMask()
    {
        if (!_showingNotification || _overlayExiting) return;
        if (!CanPresentReminder || !_notificationUi.AnimationsEnabled)
        {
            ResetNotification();
            return;
        }
        SampleOverlayProgress(null, EventArgs.Empty);
        StopOverlayProgress();
        StopNotificationOverlayMessageTicker();
        CardContent.Opacity = 1;
        _overlayExiting = true;
        if (!NotificationOverlay.IsVisible)
        {
            _ = RetreatMaskAsync(_presentation, finish: true);
            return;
        }
        NotificationOverlay.Classes.Add("leaving");
        NotificationOverlay.Opacity = 0;
    }

    private void OnOverlayOpacityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_overlayExiting && args.Property == OpacityProperty && NotificationOverlay.Opacity <= 0)
            ResetNotification();
    }

    private void ResetNotification()
    {
        ++_presentation;
        _overlayExiting = false;
        StopOverlayProgress();
        StopNotificationOverlayMessageTicker();
        NotificationMaskContent.Classes.Remove("entering");
        NotificationMaskContent.Classes.Remove("revealing");
        NotificationMaskShadow.Classes.Remove("entering");
        NotificationMaskShadow.Classes.Remove("revealing");
        NotificationMaskBg.IsOpened = false;
        ResetPage(NotificationMask, false);
        ResetPage(NotificationOverlay, false);
        NotificationOverlay.Classes.Remove("leaving");
        _hasCoursePage = _showingNotification = false;
        ApplyNotificationBackdrop(false);
        LinePanel.Height = double.NaN;
        if (_nexPanelActive) return;
        CardContent.Opacity = 1;
        IslandBackground.Height = Math.Max(CiPalette.IslandHeight, CardContent.DesiredSize.Height);
        SetIslandWidth(CardContent.Bounds.Width > 0 ? CardContent.Bounds.Width + 2 * IslandContentMargin : _normalWidth, reminder: true);
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
