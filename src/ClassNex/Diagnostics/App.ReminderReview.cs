#if DEBUG
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
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
        static async Task WaitFor(Func<bool> condition, string message, double seconds = 4)
        {
            var start = Stopwatch.GetTimestamp();
            while (!condition())
            {
                if (Stopwatch.GetElapsedTime(start).TotalSeconds > seconds) throw new InvalidOperationException(message);
                await Task.Delay(30);
            }
        }
        var settings = AppServices.Settings;
        var enabled = settings.AllowNotification;
        var effect = settings.AllowNotificationEffect;
        var originalBreakMessage = settings.Notification.ClassOffOverlayText;
        var originalDismissalMessage = settings.Notification.AfterSchoolText;
        WidgetConfig? added = null;
        var track = Element<Panel>("NotificationTrack");
        var background = Element<Border>("IslandBackground");
        var content = Element<Border>("CardContent");
        var mask = Element<Border>("NotificationMask");
        var course = Element<Grid>("NotificationOverlayCoursePage");
        var message = Element<TextBlock>("NotificationOverlayMessagePage");
        var subject = Element<TextBlock>("NotificationOverlaySubject");
        const string mode = "dark";
        var dpi = (int)Math.Round(96 * window.RenderScaling);
        bool FitsContent() => Math.Abs(background.Bounds.Width - Math.Ceiling(content.Bounds.Width + 2 * content.Margin.Left)) <= 1;
        try
        {
            await ReviewLocalChimeAsync(check);
            check(!window.Topmost && !settings.Topmost, "Non-topmost classroom fixture respects its saved setting");
            check(window.Screens.Primary is { } screen && window.GetIslandScreenRect() is { } island &&
                island.Width <= screen.WorkingArea.Width && Math.Abs(island.X + island.Width / 2.0 - (screen.WorkingArea.X + screen.WorkingArea.Width / 2.0)) <= 2,
                "Classroom island fits the working area and its visible bounds are centered at the review DPI");
            ApplyTheme(mode);
            settings.AllowNotification = true;
            settings.AllowNotificationEffect = false;
            AppServices.SaveSettings();
            notifications.DebugTriggerClassPreparing();
            await WaitFor(() => mask.IsVisible && track.IsVisible && track.Bounds.Width > 0, "The track did not start with the initial mask");
            check(track.Bounds.Width < background.Bounds.Width && track.Bounds.Width > background.Bounds.Width * 0.8,
                "Preparing mask starts its full-length progress track with an inset on both ends");
            await shot(window, "reminder-mask", mode, dpi);
            await WaitFor(() => !mask.IsVisible && course.IsEffectivelyVisible, "Preparing detail did not replace its mask", 5);
            var detailStart = Stopwatch.GetTimestamp();
            var today = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, DateTime.Now);
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
            check(!message.IsVisible && course.IsVisible, "Preparing detail remains visible through the first eight seconds");
            await WaitFor(() => message.IsEffectivelyVisible && !course.IsVisible, "The reminder never turned vertically to its message");
            check(track.IsVisible, "The progress track remains active across the ten-second message turn");
            await shot(window, "reminder-message", mode, dpi);
            settings.AllowNotification = false;
            AppServices.SaveSettings();
            await WaitFor(() => !track.IsVisible && FitsContent(), "New classroom width did not recover when notifications were disabled");
            await shot(window, "reminder-restored", mode, dpi);
            settings.AllowNotification = true;
            AppServices.SaveSettings();
            notifications.DebugTriggerClassOn();
            await WaitFor(() => mask.IsVisible, "Class-on reminder missing");
            AppServices.Widgets.Remove(added);
            added = null;
            await WaitFor(() => !track.IsVisible && FitsContent(), "Classroom width did not shrink when class-on reminder expired", 7);
            settings.Notification.ClassOffOverlayText = "离开座位活动一下，下节课见。";
            notifications.DebugTriggerClassOff();
            await WaitFor(() => !mask.IsVisible && course.IsEffectivelyVisible && track.IsVisible, "Break did not show the next lesson");
            var breakDetailStart = Stopwatch.GetTimestamp();
            check(subject.Text == expected, "Break details use the full next lesson, not its short timetable label");
            await shot(window, "reminder-break", mode, dpi);
            await WaitFor(() => message.IsEffectivelyVisible && !course.IsVisible && message.Text == settings.Notification.ClassOffOverlayText,
                "Break did not turn to its custom message", 12);
            check(Stopwatch.GetElapsedTime(breakDetailStart).TotalSeconds >= 9 && track.IsVisible,
                "The break message follows a ten-second course page without restarting the track");
            await shot(window, "reminder-break-message", mode, dpi);
            await WaitFor(() => !track.IsVisible && FitsContent(), "Classroom width did not recover after break", 12);
            check(true, "Class-on and break expiry both restore the measured classroom width");
            settings.Notification.AfterSchoolText = "请带好随身物品，明天见。";
            notifications.DebugTriggerAfterSchool();
            await WaitFor(() => mask.IsEffectivelyVisible && Element<TextBlock>("NotificationMaskText").Text == "放学", "Dismissal mask missing");
            await WaitFor(() => !mask.IsVisible && message.IsEffectivelyVisible && !course.IsVisible, "Dismissal showed an empty next-lesson page", 5);
            check(track.IsVisible && message.Text == settings.Notification.AfterSchoolText, "Dismissal goes straight to its custom message");
            await shot(window, "reminder-after-school", mode, dpi);
            settings.AllowNotification = false;
            AppServices.SaveSettings();
            await WaitFor(() => !track.IsVisible && FitsContent(), "Disabling dismissal did not restore the measured classroom width");
            settings.AllowNotification = true;
            AppServices.SaveSettings();
            window.EnterEditMode();
            check(!window.CanPresentReminder && !notifications.Publish(new("review/edit", "1", DateTimeOffset.Now.AddMinutes(1), TimeSpan.Zero, TimeSpan.FromSeconds(1))),
                "Editing components suppresses classroom reminders");
            window.ExitEditMode();
            window.ShowNexPanel("课堂问答测试", false);
            check(!window.CanPresentReminder && !track.IsVisible && !content.IsVisible && !window.Topmost, "NEX, schedule and reminder surfaces do not overlap or force topmost");
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
