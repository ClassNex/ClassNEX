#if DEBUG
using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewRemindersAsync(MainWindow window, Func<Window, string, string, int, Task> shot, Action<bool, string> check)
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
        var canvas = Element<Canvas>("ProgressContainer");
        var overlay = Element<Border>("NotificationOverlay");
        var background = Element<Border>("IslandBackground");
        var content = Element<Border>("CardContent");
        var mask = Element<Border>("NotificationMask");
        var course = Element<Grid>("NotificationOverlayCoursePage");
        var message = Element<TextBlock>("NotificationOverlayMessagePage");
        var simple = Element<TextBlock>("NotificationSimpleMessage");
        var slides = Element<ListBox>("NotificationSlides");
        bool OnSlide(int page) => slides.SelectedIndex == page && (page == 0 ? course.IsEffectivelyVisible : message.IsEffectivelyVisible) &&
            (page == 0 ? (Control)course : message).GetVisualAncestors().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                .Any(p => p.Name == "ContentPresenter" && p.Opacity > .98 && p.IsEffectivelyVisible);
        var subject = Element<TextBlock>("NotificationOverlaySubject");
        async Task WaitFor(Func<bool> condition, string message, double seconds = 4)
        {
            var start = Stopwatch.GetTimestamp();
            while (!condition())
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
            notifications.DebugTriggerClassPreparing();
            await WaitFor(() => mask.IsEffectivelyVisible, "Preparing mask did not appear");
            check(!track.IsEffectivelyVisible, "The preparing mask has no detail progress rail");
            await shot(window, "reminder-mask-opening", mode, dpi);
            var face = Element<Border>("NotificationMaskContent");
            var stripes = Element<Controls.SlantedMaskControl>("NotificationMaskBg");
            await WaitFor(() => mask.IsEffectivelyVisible && face.Opacity >= .995 && stripes.TransitionCompleted.IsCompletedSuccessfully &&
                face.RenderTransform is TransformGroup transforms && transforms.Children.OfType<ScaleTransform>().Any(scale => Math.Abs(scale.ScaleX - 1) < .002), "Preparing title did not settle at full size and opacity", 2);
            await shot(window, "reminder-mask", mode, dpi);
            await WaitFor(() => !mask.IsVisible && course.IsEffectivelyVisible && track.IsEffectivelyVisible && track.Bounds.Width > 0,
                "Preparing detail with its progress rail did not replace the mask", 5);
            var detailStart = Stopwatch.GetTimestamp();
            var prepareStartValue = track.Value;
            if (new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
                check(background.Transitions?.OfType<DoubleTransition>().Any(t => t.Property == Layoutable.WidthProperty && t.Duration == TimeSpan.FromMilliseconds(300)) == true,
                    "The reminder background uses the original 300 ms resize independently of detail layout");
            check(prepareStartValue is > 0.92 and <= 1 && track.Minimum == 0 && track.Maximum == 1 && track.MinWidth == 0 && !track.IsIndeterminate,
                "Preparing progress starts near full for its own session, excluding time spent in the mask");
            check(track.Transitions?.OfType<DoubleTransition>().Any(t => t.Property == ProgressBar.ValueProperty &&
                t.Duration == TimeSpan.FromMilliseconds(50) && t.Easing is LinearEasing) == true,
                "Native progress values use the upstream 50 ms linear transition");
            CheckRailBounds();
            var today = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, AppServices.ClassroomNow);
            var expected = (today.Next ?? today.Slots.FirstOrDefault())?.Subject ?? "演示课程";
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
            var remaining = TimeSpan.FromSeconds(8.5) - Stopwatch.GetElapsedTime(detailStart);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            check(OnSlide(0), "Preparing detail remains visible through the first eight seconds");
            await WaitFor(() => OnSlide(1), "The reminder never turned vertically to its message");
            check(track.IsEffectivelyVisible && track.Value > 0 && track.Value < prepareStartValue - 0.15,
                "The preparing message turn keeps the already elapsed detail progress");
            await shot(window, "reminder-message", mode, dpi);
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
            await WaitFor(() => OnSlide(1) && message.Text == settings.Notification.ClassOffOverlayText,
                "Break did not turn to its custom message", 12);
            var breakElapsed = Stopwatch.GetElapsedTime(breakDetailStart).TotalSeconds;
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
            window.ShowNexPanel("课堂问答测试", false);
            check(!window.CanPresentReminder && !track.IsEffectivelyVisible && !content.IsVisible && !window.Topmost, "NEX, schedule and reminder surfaces do not overlap or force topmost");
            window.HideNexPanel();
            window.Hide();
            check(!window.CanPresentReminder, "A hidden classroom cannot receive reminders");
            window.Show();
            check(window.CanPresentReminder && content.IsVisible, "Restoring the classroom restores both content and its notification channel");
        }
        finally
        {
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
