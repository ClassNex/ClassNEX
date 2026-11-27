#if DEBUG
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewRemindersAsync(MainWindow window, Func<Window, string, string, int, Task> shot, Action<Window, string, string, int> captureFrame, Action<bool, string> check)
    {
        if (AppServices.NotificationService is not NotificationService notifications || AccountService.Current != null || AppServices.Settings.CentralManagement.IsEnabled)
            throw new InvalidOperationException("Reminder review requires an anonymous desktop fixture");
        T Element<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new InvalidOperationException(name);
        var settings = AppServices.Settings;
        var enabled = settings.AllowNotification;
        var effect = settings.AllowNotificationEffect;
        var originalBreakMessage = settings.Notification.ClassOffOverlayText;
        var originalDismissalMessage = settings.Notification.AfterSchoolText;
        WidgetConfig? added = null;
        var track = Element<ProgressBar>("OverlayTimeProgressBar");
        var hover = Element<Panel>("HoverHost");
        var maskShadow = Element<Border>("NotificationMaskShadow");
        var canvas = Element<Canvas>("ProgressContainer");
        var overlay = Element<Border>("NotificationOverlay");
        var background = Element<Border>("IslandBackground");
        var content = Element<Border>("CardContent");
        var mask = Element<Border>("NotificationMask");
        var course = Element<Grid>("NotificationOverlayCoursePage");
        var message = Element<TextBlock>("NotificationOverlayMessagePage");
        var simple = Element<TextBlock>("NotificationSimpleMessage");
        var slides = Element<ListBox>("NotificationSlides");
        ContentPresenter? Presenter(int page) => slides.ContainerFromIndex(page)?.GetVisualDescendants().OfType<ContentPresenter>()
            .FirstOrDefault(p => p.Name == "ContentPresenter");
        bool OnSlide(int page) => slides.SelectedIndex == page && (page == 0 ? course.IsEffectivelyVisible : message.IsEffectivelyVisible) &&
            Presenter(page) is { IsEffectivelyVisible: true, Opacity: > .98 };
        bool StaticSlide(int page) => OnSlide(page) && Presenter(page)?.RenderTransform is null && Presenter(1 - page) is { IsVisible: false };
        var subject = Element<TextBlock>("NotificationOverlaySubject");
        async Task WaitFor(Func<bool> condition, string message, double seconds = 4, bool afterUiUpdates = false)
        {
            var start = Stopwatch.GetTimestamp();
            while (!(afterUiUpdates ? await Dispatcher.UIThread.InvokeAsync(condition, DispatcherPriority.Background) : condition()))
            {
                if (Stopwatch.GetElapsedTime(start).TotalSeconds > seconds)
                {
                    static string State(Control control) => $"{control.Name}:{control.IsVisible}/{control.IsEffectivelyVisible},opacity={control.Opacity:0.00},bounds={control.Bounds}";
                    var page = course.GetVisualAncestors().OfType<Control>().Select(State);
                    throw new InvalidOperationException($"{message}; clock={AppServices.ClassroomNow:HH:mm:ss}, available={window.CanPresentReminder}, mask={Element<TextBlock>("NotificationMaskText").Text}, subject={subject.Text}, selected={slides.SelectedIndex}/{slides.ItemCount}; {State(mask)}; {State(overlay)}; {State(track)}; course path={string.Join(" > ", page)}");
                }
                await Task.Delay(30);
            }
        }
        const string mode = "dark";
        var dpi = (int)Math.Round(96 * window.RenderScaling);
        bool FitsContent() => Math.Abs(background.Bounds.Width - Math.Ceiling(content.Bounds.Width + 2 * content.Margin.Left)) <= 1;
        void CheckRailBounds()
        {
            check(track.Parent == canvas && track.IsEffectivelyVisible && track.Bounds.Height > 0 &&
                track.TranslatePoint(default, overlay) is { } origin && Math.Abs(origin.X) <= 1 &&
                Math.Abs(origin.Y + track.Bounds.Height - overlay.Bounds.Height) <= 1 &&
                Math.Abs(track.Bounds.Width - overlay.Bounds.Width) <= 1 &&
                overlay.ClipToBounds && overlay.CornerRadius.BottomLeft > 0 && overlay.CornerRadius.BottomRight > 0,
                "The native rail spans the full bottom edge of the rounded detail overlay without sizing its parent");
        }
        try
        {
            await ReviewLocalChimeAsync(check);
            check(!window.Topmost && !settings.Topmost, "Non-topmost classroom fixture respects its saved setting");
            bool Centered() => window.Screens.Primary is { } screen && window.GetIslandScreenRect() is { } island &&
                island.Width <= screen.WorkingArea.Width && Math.Abs(island.X + island.Width / 2.0 - (screen.WorkingArea.X + screen.WorkingArea.Width / 2.0)) <= 2;
            await WaitFor(Centered, $"The island did not settle after the DPI change (initial island {window.GetIslandScreenRect()}, work {window.Screens.Primary?.WorkingArea})");
            check(Centered(), "Classroom island fits the working area and its visible bounds are centered at the review DPI");
            ApplyTheme(mode);
            settings.AllowNotification = true;
            settings.AllowNotificationEffect = false;
            AppServices.SaveSettings();
            var previewStart = Stopwatch.GetTimestamp();
            notifications.DebugTriggerClassPreparing();
            await WaitFor(() => mask.IsEffectivelyVisible, "Preparing mask did not appear");
            check(!track.IsEffectivelyVisible, "The preparing mask has no detail progress rail");
            await shot(window, "reminder-mask-opening", mode, dpi);
            var face = Element<Border>("NotificationMaskContent");
            var stripes = Element<Controls.SlantedMaskControl>("NotificationMaskBg");
            await WaitFor(() => mask.IsEffectivelyVisible && face.Opacity >= .995 && stripes.TransitionCompleted.IsCompletedSuccessfully &&
                face.RenderTransform is TransformGroup transforms && transforms.Children.OfType<ScaleTransform>().Any(scale => Math.Abs(scale.ScaleX - 1) < .002), "Preparing title did not settle at full size and opacity", 2);
            await shot(window, "reminder-mask", mode, dpi);
            var countdown = window.Find<Avalonia.Controls.Documents.Run>("NotificationSeconds") ?? throw new InvalidOperationException("NotificationSeconds");
            await WaitFor(() => course.IsEffectivelyVisible && track.IsEffectivelyVisible && track.Bounds.Width > 0 && int.TryParse(countdown.Text, out _),
                "Preparing detail did not render its countdown and rail", 5, afterUiUpdates: true);
            var detailStart = Stopwatch.GetTimestamp();
            var prepareStartValue = track.Value;
            if (DesktopMotion.IsEnabled)
                check(background.Transitions?.OfType<DoubleTransition>().Any(t => t.Property == Layoutable.WidthProperty && t.Duration == TimeSpan.FromMilliseconds(300)) == true,
                    "The reminder background uses the original 300 ms resize independently of detail layout");
            check(prepareStartValue is > 0.92 and <= 1 && track.Minimum == 0 && track.Maximum == 1 && track.MinWidth == 0 && !track.IsIndeterminate,
                "Preparing progress starts near full for its own session, excluding time spent in the mask");
            var firstSeconds = int.Parse(countdown.Text!);
            var now = AppServices.ClassroomNow;
            var nextLesson = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now).Next ?? throw new InvalidOperationException("The capped preview fixture needs a scheduled next lesson");
            var lessonStart = now.Date + nextLesson.Start;
            check(firstSeconds > 23 && Math.Abs((lessonStart - now).TotalSeconds - firstSeconds) <= 1,
                "The first rendered countdown targets the timetable, beyond the preview's 23-second lifetime");
            var detailSeconds = window.ReviewOverlayDuration.TotalSeconds;
            check(detailSeconds > 23, "The service issued a real course-length progress clock independently of the short preview cap");
            var lastSeconds = firstSeconds;
            var lastRail = prepareStartValue;
            var lastSampleAt = TimeSpan.Zero;
            void SamplePreparingClock()
            {
                var clockLeft = (lessonStart - AppServices.ClassroomNow).TotalSeconds;
                var rail = track.Value;
                var sampledAt = Stopwatch.GetElapsedTime(previewStart);
                if (!track.IsEffectivelyVisible || !int.TryParse(countdown.Text, out var shown) ||
                    Math.Abs(shown - clockLeft) > 1 || Math.Abs(rail * detailSeconds - clockLeft) > 1 || Math.Abs(rail * detailSeconds - shown) > 1.25)
                    throw new InvalidOperationException($"Preparing clocks diverged: timetable {clockLeft:F2}s, label '{countdown.Text}', rendered rail {rail * detailSeconds:F2}s ({rail:F6} × {detailSeconds:F2}s), rail target {window.CountdownProgressValue * detailSeconds:F2}s, previous sample {(sampledAt - lastSampleAt).TotalMilliseconds:F0}ms ago");
                (lastSeconds, lastRail, lastSampleAt) = (shown, rail, sampledAt);
            }
            await WaitFor(() => { SamplePreparingClock(); return !mask.IsVisible; }, "Preparing detail did not replace the mask", 5, afterUiUpdates: true);
            if (DesktopMotion.IsEnabled)
            {
                check(track.Transitions?.OfType<DoubleTransition>().Any(t => t.Property == ProgressBar.ValueProperty &&
                    t.Duration == TimeSpan.FromMilliseconds(50) && t.Easing is LinearEasing) == true,
                    "Native progress values use the upstream 50 ms linear transition");
                check(hover.Transitions?.OfType<DoubleTransition>().Any(t => t.Property == Visual.OpacityProperty && t.Duration == TimeSpan.FromMilliseconds(100)) == true,
                    "The classroom hover surface retains its 100 ms opacity transition");
            }
            else check(track.Transitions is null && hover.Transitions is null && maskShadow.Transitions is null,
                "Reduced motion removes value, hover opacity and shadow interpolation from the live controls");
            CheckRailBounds();
            var expected = nextLesson.Subject;
            check(subject.Text == expected,
                "Preparing detail displays the full next course from the live timetable");
            check(subject.TranslatePoint(default, background) is { } at && at.X >= 0 && at.Y >= 0 &&
                at.X + subject.Bounds.Width <= background.Bounds.Width + 1 && at.Y + subject.Bounds.Height <= background.Bounds.Height + 1 &&
                subject.TextLayout.Height <= subject.Bounds.Height + 1, "The full lesson text fits the visible reminder, including wrapped lines");
            await shot(window, "reminder-subject", mode, dpi);
            var normalBefore = content.Bounds.Width;
            added = AppServices.Widgets.Add("text");
            added.Text = "课堂布局验证";
            AppServices.Widgets.Save();
            await WaitFor(() => content.Bounds.Width > normalBefore + 8, "Underlying schedule content did not grow during the reminder");
            await WaitFor(() => { SamplePreparingClock(); return Stopwatch.GetElapsedTime(detailStart).TotalSeconds >= 8.5; },
                "Preparing rail did not reach the first page sample", 10, afterUiUpdates: true);
            check(OnSlide(0), "Preparing detail remains visible through the first eight seconds");
            DesktopMotion.SetReviewEnabled(true);
            await WaitFor(() => track.Transitions != null && OnSlide(0), "Motion change lost the current preparing page");
            var selectionChanged = false;
            void ObserveSelection(object? sender, SelectionChangedEventArgs e) => selectionChanged = true;
            slides.SelectionChanged += ObserveSelection;
            try
            {
                DesktopMotion.SetReviewEnabled(false);
                await WaitFor(() => track.Transitions is null && StaticSlide(0), "The live course lost its selection or kept its vertical transition after motion was disabled", 1);
                check(!selectionChanged, "Changing motion reuses the same list items without even temporarily deselecting the displayed course");
            }
            finally { slides.SelectionChanged -= ObserveSelection; }
            await WaitFor(() => { SamplePreparingClock(); return OnSlide(1); }, "The reminder did not switch to its message on the original clock", afterUiUpdates: true);
            var firstTurn = Stopwatch.GetElapsedTime(detailStart).TotalSeconds;
            check(StaticSlide(1) && firstTurn is >= 9 and < 12 && window.ReviewOverlayDuration.TotalSeconds == detailSeconds,
                $"Disabling motion during the course produces a stationary message at {firstTurn:F2}s without resetting the ten-second page or the bell clock");
            check(lastSeconds < firstSeconds && lastRail < prepareStartValue,
                "The rail and countdown follow the same lesson clock throughout the first page and its message turn");
            await shot(window, "reminder-message", mode, dpi);
            DesktopMotion.SetReviewEnabled(true);
            await WaitFor(() => track.Transitions != null, "Enabling motion did not restore the live rail transition");
            check(StaticSlide(1), "Re-enabling motion leaves the displayed message stationary until the next page turn");
            DesktopMotion.SetReviewEnabled(null);
            await WaitFor(() =>
            {
                if (!track.IsEffectivelyVisible) return true;
                if (overlay.Opacity >= .999) SamplePreparingClock();
                return false;
            }, "Preparing preview did not close at its own 23-second deadline", 16, afterUiUpdates: true);
            var closedAt = Stopwatch.GetElapsedTime(previewStart).TotalSeconds;
            check(lastSampleAt.TotalSeconds >= 22.5 && closedAt is >= 22.9 and <= 25 && lastSeconds > 0 && lastRail > 0,
                $"Preparing preview closed after {closedAt:F2}s with {lastSeconds}s still on the timetable and rail ({lastRail:P2}); it did not drain early");
            notifications.DebugTriggerClassPreparing();
            await WaitFor(() => !mask.IsVisible && track.IsEffectivelyVisible, "No preparing detail available for the disable-while-visible check", 6);
            settings.AllowNotification = false;
            AppServices.SaveSettings();
            await WaitFor(() => !track.IsEffectivelyVisible && FitsContent(), "New classroom width did not recover when notifications were disabled");
            await shot(window, "reminder-restored", mode, dpi);
            settings.AllowNotification = true;
            AppServices.SaveSettings();
            notifications.DebugTriggerClassOn();
            await WaitFor(() => mask.IsVisible, "Class-on reminder missing");
            var railSeenInClassOn = track.IsEffectivelyVisible;
            AppServices.Widgets.Remove(added);
            added = null;
            await WaitFor(() =>
            {
                railSeenInClassOn |= track.IsEffectivelyVisible;
                return !mask.IsVisible && FitsContent();
            }, "Classroom width did not shrink when class-on reminder expired", 7);
            check(!railSeenInClassOn, "A mask-only class-on reminder never displays a rail");
            settings.Notification.ClassOffOverlayText = "离开座位活动一下，下节课见。";
            notifications.DebugTriggerClassOff();
            await WaitFor(() => mask.IsEffectivelyVisible, "Break mask missing");
            check(!track.IsEffectivelyVisible, "The break mask does not show the previous detail rail");
            await WaitFor(() => !mask.IsVisible && course.IsEffectivelyVisible && track.IsEffectivelyVisible, "Break did not show the next lesson");
            var breakDetailStart = Stopwatch.GetTimestamp();
            var breakStartValue = track.Value;
            check(breakStartValue is > 0.92 and <= 1, "A new break starts a fresh detail progress session");
            CheckRailBounds();
            var lastValue = breakStartValue;
            var decreases = 0;
            for (var i = 0; i < 7; i++)
            {
                await Task.Delay(80);
                var value = track.Value;
                if (value > lastValue + 0.001) throw new InvalidOperationException("The break rail ran backwards");
                if (value < lastValue - 0.0001) decreases++;
                lastValue = value;
            }
            check(decreases >= 3, "The rail advances between whole-second ticks even when no countdown label changes");
            check(subject.Text == expected, "Break details use the full next lesson, not its short timetable label");
            await shot(window, "reminder-break", mode, dpi);
            DesktopMotion.SetReviewEnabled(false);
            await WaitFor(() => track.Transitions is null && StaticSlide(0), "The break did not settle its course page");
            DesktopMotion.SetReviewEnabled(true);
            await WaitFor(() => track.Transitions != null && StaticSlide(0), "The break replayed the current page when motion was enabled");
            await WaitFor(() => OnSlide(1) && message.Text == settings.Notification.ClassOffOverlayText,
                "Break did not turn to its custom message", 12);
            var breakElapsed = Stopwatch.GetElapsedTime(breakDetailStart).TotalSeconds;
            check(Presenter(1)?.RenderTransform is TransformGroup && breakElapsed is >= 9 and < 12,
                $"Re-enabling motion restores the vertical presenter on the next scheduled break page at {breakElapsed:F2}s");
            DesktopMotion.SetReviewEnabled(null);
            check(breakElapsed >= 9 && track.IsEffectivelyVisible && Math.Abs(track.Value - (breakStartValue - breakElapsed / 20)) < 0.045,
                "The break message turn stays on the twenty-second detail timeline, excluding its two-second mask");
            var indicator = track.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "PART_Indicator");
            var trough = track.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "ProgressBarTrack");
            check(indicator.Transitions?.OfType<DoubleTransition>().Any(t => t.Property == Control.WidthProperty) != true &&
                track.Height == 3 && trough.Height == 1 &&
                Math.Abs(indicator.Bounds.Height - 3) <= 1 / window.RenderScaling && Math.Abs(trough.Bounds.Height - 1) <= 1 / window.RenderScaling &&
                trough.Background is ISolidColorBrush { Color.A: > 0 },
                "FluentAvalonia 2.4.1 renders a three-DIP indicator over a one-DIP track, without a second width animation");
            check(indicator.IsEffectivelyVisible && indicator.TranslatePoint(default, track) is { } left && Math.Abs(left.X) <= 2 &&
                indicator.TranslatePoint(new Point(indicator.Bounds.Width, 0), track) is { } right &&
                Math.Abs((right.X - left.X) / track.Bounds.Width - track.Value) < 0.04,
                "The rendered Fluent indicator stays left-anchored and its width matches remaining detail time");
            await shot(window, "reminder-break-message", mode, dpi);
            await WaitFor(() => track.IsEffectivelyVisible && track.Value is > 0 and < 0.1, "The break rail disappeared before its final tenth", 12);
            await shot(window, "reminder-break-ending", mode, dpi);
            await WaitFor(() => !track.IsEffectivelyVisible && FitsContent(), "Classroom width did not recover after break");
            check(true, "Class-on and break expiry both restore the measured classroom width");
            settings.Notification.AfterSchoolText = "请带好随身物品，明天见。";
            notifications.DebugTriggerAfterSchool();
            await WaitFor(() => mask.IsEffectivelyVisible && Element<TextBlock>("NotificationMaskText").Text == "放学", "Dismissal mask missing");
            check(!track.IsEffectivelyVisible, "The dismissal mask has no progress rail");
            await WaitFor(() => !mask.IsVisible && simple.IsEffectivelyVisible && !slides.IsEffectivelyVisible, "Dismissal showed an empty next-lesson page", 7);
            check(track.IsEffectivelyVisible && track.Value is > 0.95 and <= 1 && simple.Text == settings.Notification.AfterSchoolText,
                "Dismissal goes straight to its custom message with fresh detail progress");
            await shot(window, "reminder-after-school", mode, dpi);
            settings.AllowNotification = false;
            AppServices.SaveSettings();
            await WaitFor(() => !track.IsEffectivelyVisible && FitsContent(), "Disabling dismissal did not restore the measured classroom width");
            settings.AllowNotification = true;
            AppServices.SaveSettings();
            window.EnterEditMode();
            check(!window.CanPresentReminder && !notifications.Publish(new("review/edit", "1", AppServices.ClassroomClock.GetLocalNow().AddMinutes(1), TimeSpan.Zero, TimeSpan.FromSeconds(1))),
                "Editing components suppresses classroom reminders");
            window.ExitEditMode();
            var nex = Element<Border>("NexPanel");
            window.ShowNotificationMask("下课");
            window.ShowNotificationOverlay("", "", "", "", message: "整理课桌\n擦净黑板\n关好门窗\n带好作业和书包");
            await WaitFor(() => !mask.IsVisible && simple.IsEffectivelyVisible && simple.TextLayout.TextLines.Count >= 4 && background.Bounds.Height > nex.Height + 10,
                "The multi-line reminder did not grow before NEX took over");
            var previousOrientation = settings.Orientation;
            try
            {
                window.ShowNexPanel("课堂问答测试", false);
                await WaitFor(() => nex.IsEffectivelyVisible && Math.Abs(background.Bounds.Height - nex.Bounds.Height) <= 1 &&
                    Math.Abs(background.Bounds.Width - nex.Bounds.Width) <= 1, "NEX inherited the size of the interrupted multi-line reminder");
                check(!window.CanPresentReminder && !overlay.IsVisible && !track.IsEffectivelyVisible && !content.IsVisible && !window.Topmost,
                    "NEX takes over a multi-line reminder at its own measured size, without overlapping the timetable or forcing topmost");

                settings.Orientation = LayoutOrientation.Vertical;
                window.ApplySettings();
                var nexHeight = background.Bounds.Height;
                var hiddenDesired = content.DesiredSize;
                window.HideNexPanel();
                var handoffHeight = background.Height;
                var firstWidgets = content.Bounds.Size;
                var firstStack = Element<StackPanel>("WidgetHost").DesiredSize;
                await WaitFor(() => content.IsEffectivelyVisible && content.Bounds.Height > nex.Height + 5 && FitsContent() &&
                    Math.Abs(background.Bounds.Height - content.Bounds.Height) <= 1, "Leaving NEX did not restore the current vertical widget layout");
                check(Math.Abs(handoffHeight - content.Bounds.Height) <= 1 && Math.Abs(firstWidgets.Height - content.Bounds.Height) <= 1 &&
                      window.CanPresentReminder && !nex.IsVisible && content.Clip is null,
                    $"Returning from NEX uses the newly measured widget height on the first layout, including changes made while the classroom was hidden; NEX {nexHeight:0.##} DIP, hidden desired {hiddenDesired}, first island target {handoffHeight:0.##} DIP, first classroom {firstWidgets}, first widget stack {firstStack}, settled classroom {content.Bounds.Size}, settled island {background.Bounds.Size}, available {window.CanPresentReminder}, NEX visible {nex.IsVisible}, clipped {content.Clip is not null}");
            }
            finally
            {
                window.HideNexPanel();
                settings.Orientation = previousOrientation;
                window.ApplySettings();
            }
            window.Hide();
            check(!window.CanPresentReminder, "A hidden classroom cannot receive reminders");
            window.Show();
            check(window.CanPresentReminder && content.IsVisible, "Restoring the classroom restores both content and its notification channel");
            await ReviewReminderHandoffAsync(window, shot, captureFrame, check, mode, dpi);
            await ReviewReminderGeometryAsync(window, shot, check);
            DesktopMotion.SetReviewEnabled(true);
            await WaitFor(() => track.Transitions != null, "Could not enable opening motion in the anonymous fixture");
            window.ShowNotificationMask("准备上课");
            DesktopMotion.SetReviewEnabled(false);
            await Dispatcher.UIThread.InvokeAsync(() => check(mask.IsVisible && !overlay.IsVisible && face.Opacity >= .999 &&
                stripes is { IsOpened: true, Region0Progress: 1, Region4Progress: 1 } && stripes.TransitionCompleted.IsCompletedSuccessfully &&
                !face.Classes.Contains("entering"), "Disabling motion mid-opening settles the title and its mask in the next UI turn: " +
                $"motion={DesktopMotion.IsEnabled}, available={window.CanPresentReminder}, mask={mask.IsVisible}, overlay={overlay.IsVisible}, " +
                $"title opacity={face.Opacity:0.0000}, transform={face.RenderTransform?.Value}, entering={face.Classes.Contains("entering")}, " +
                $"stripes open={stripes.IsOpened}, progress=({stripes.Region0Progress:0.0000}, {stripes.Region1Progress:0.0000}, " +
                $"{stripes.Region2Progress:0.0000}, {stripes.Region3Progress:0.0000}, {stripes.Region4Progress:0.0000}), completion={stripes.TransitionCompleted.Status}"), DispatcherPriority.Background);
            DesktopMotion.SetReviewEnabled(true);
            await WaitFor(() => track.Transitions != null, "Could not re-enable motion for the detail reveal");
            window.ShowNotificationOverlay("", "", "", "", message: "测试提醒");
            DesktopMotion.SetReviewEnabled(false);
            await Dispatcher.UIThread.InvokeAsync(() => check(!mask.IsVisible && simple.IsEffectivelyVisible &&
                stripes is { IsOpened: false, Region0Progress: 0, Region4Progress: 0 } && stripes.TransitionCompleted.IsCompletedSuccessfully,
                "Disabling motion during the title retreat reveals the current detail without replaying the title"), DispatcherPriority.Background);
            DesktopMotion.SetReviewEnabled(true);
            await WaitFor(() => track.Transitions != null, "Could not re-enable motion for the exit");
            window.HideNotificationMask();
            DesktopMotion.SetReviewEnabled(false);
            await Dispatcher.UIThread.InvokeAsync(() => check(!mask.IsVisible && !overlay.IsVisible && content.Opacity >= .999 && content.Clip is null &&
                background.Transitions is null, "Disabling motion mid-exit cancels the handoff and restores the unobstructed classroom in the next UI turn"), DispatcherPriority.Background);
            DesktopMotion.SetReviewEnabled(null);
            if (!DesktopMotion.IsEnabled)
            {
                var oldValue = window.CountdownProgressValue;
                var oldOpacity = hover.Opacity;
                try
                {
                    window.CountdownProgressValue = .37;
                    hover.Opacity = .62;
                    check(Math.Abs(track.Value - .37) < .0001 && Math.Abs(hover.Opacity - .62) < .0001,
                        "The real rail and hover surface reach their new values in the same event turn with animations off");
                }
                finally { window.CountdownProgressValue = oldValue; hover.Opacity = oldOpacity; }
            }
        }
        finally
        {
            DesktopMotion.SetReviewEnabled(null);
            if (added != null) AppServices.Widgets.Remove(added);
            settings.AllowNotification = enabled;
            settings.AllowNotificationEffect = effect;
            settings.Notification.ClassOffOverlayText = originalBreakMessage;
            settings.Notification.AfterSchoolText = originalDismissalMessage;
            AppServices.SaveSettings();
        }
    }

    private static async Task ReviewLocalChimeAsync(Action<bool, string> check)
    {
        var data = new DirectoryInfo(SettingsService.DataDirectory);
        if (data.Parent is not { } fixture || !File.Exists(Path.Combine(fixture.FullName, ".classnex-review")))
            throw new InvalidOperationException("Chime review requires an isolated review directory");
        var good = NexChime.BuildClassroomBell();
        var bad = (byte[])good.Clone();
        bad[36] = 0;
        check(ReminderSound.IsShortWave(good) && !ReminderSound.IsShortWave(good.AsSpan(0, 12)) && !ReminderSound.IsShortWave(bad),
            "Local chime accepts an actual short PCM bell; rejects a truncated header or missing audio samples");
        var source = Path.Combine(fixture.FullName, "review-chime.wav");
        await File.WriteAllBytesAsync(source, good);
        await ReminderSound.ImportAsync(source);
        await File.WriteAllBytesAsync(source, bad);
        try { await ReminderSound.ImportAsync(source); throw new InvalidOperationException("Invalid replacement sound was accepted"); }
        catch (InvalidDataException) { }
        var saved = await File.ReadAllBytesAsync(Path.Combine(data.FullName, "NotificationChime.wav"));
        check(saved.AsSpan().SequenceEqual(good) && ReminderSound.HasCustomSound(new() { UseLocalReminderSound = true }) && !ReminderSound.HasCustomSound(new()),
            "Invalid replacement preserves imported WAV; restoring the default does not delete the saved sound");
    }
}
#endif
