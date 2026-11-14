using System.Speech.Synthesis;
using Avalonia;
using Avalonia.Threading;
using ClassNex.Controls.NotificationEffects;
using ClassNex.Models;
using ClassNex.Styles;
using ClassNex.Views;

namespace ClassNex.Services;

public sealed class NotificationService
{
    private readonly DispatcherTimer _timer;
    private readonly TopmostEffectWindow _effectWindow;

    private string _lastKey = "\0";
    private string? _prepareKey;
    private bool _started;
    private int _displayGeneration;
    private SpeechSynthesizer? _speech;

    public NotificationService(TopmostEffectWindow effectWindow)
    {
        _effectWindow = effectWindow;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _lastKey = CurrentKey(DateTime.Now);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        _started = false;
        _prepareKey = null;
        _speech?.SpeakAsyncCancelAll();
    }

    private void OnTick()
    {
        if (!AppServices.Settings.AllowNotification)
            return;

        var now = DateTime.Now;
        var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        var current = FindCurrent(summary.Slots, now.TimeOfDay);
        var key = CurrentKey(summary, now.TimeOfDay, current);

        if (key != _lastKey)
        {
            _lastKey = key;
            _prepareKey = null;
            if (key.StartsWith("course:", StringComparison.Ordinal))
                ShowClassOn(now, current);
            else if (key.StartsWith("break:", StringComparison.Ordinal))
                ShowClassOff(now, current, summary);
        }

        if (current is null || current.Value.Title == "课间休息")
            TryShowPreparing(summary, now);
        else
            _prepareKey = null;
    }

    private void TryShowPreparing(TodaySummary summary, DateTime now)
    {
        var settings = AppServices.Settings;
        var notification = settings.Notification;
        if (!settings.AllowMinorNotification ||
            !notification.IsClassOnPreparingNotificationEnabled)
        {
            _prepareKey = null;
            return;
        }

        var next = summary.Slots.FirstOrDefault(slot => slot.Start > now.TimeOfDay);
        if (next is null)
        {
            _prepareKey = null;
            return;
        }

        var threshold = next.IsOutDoor
            ? notification.OutDoorClassPreparingDeltaTime
            : notification.InDoorClassPreparingDeltaTime;
        var remaining = next.Start - now.TimeOfDay;
        if (remaining <= TimeSpan.Zero || remaining > TimeSpan.FromSeconds(Math.Max(0, threshold)))
        {
            _prepareKey = null;
            return;
        }

        var key = $"prepare:{next.Start}:{next.Subject}";
        if (_prepareKey == key)
            return;

        _prepareKey = key;
        ShowPreparing(next, remaining, notification);
    }

    private void ShowPreparing(CourseSlot next, TimeSpan remaining, NotificationSettings notification)
    {
        var mainWindow = NotificationWindow;
        if (mainWindow is null)
            return;

        var mask = next.IsOutDoor
            ? notification.OutdoorClassOnPreparingMaskText
            : notification.ClassOnPreparingMaskText;
        var message = next.IsOutDoor
            ? notification.OutdoorClassOnPreparingText
            : notification.ClassOnPreparingText;
        var teacher = notification.ShowTeacherName ? next.Teacher ?? "" : "";
        var speech = $"距上课还剩{FormatDuration(remaining)}。{message}下节课是{next.DisplayName}" +
                     (teacher.Length > 0 ? $"，由{teacher}老师任教。" : "。");

        var generation = BeginDisplay();
        ShowMask(mainWindow, mask);
        Schedule(generation, TimeSpan.FromMilliseconds(1200), () =>
            mainWindow.ShowNotificationOverlay(message, next.DisplayName, teacher, next.TimeRange));
        Schedule(generation, TimeSpan.FromSeconds(8), mainWindow.HideNotificationMask);
        Speak(notification.IsSpeechEnabledOnClassPreparing, speech);
    }

    private void ShowClassOn(DateTime now, (string Title, TimeSpan Start, TimeSpan End)? current)
    {
        var notification = AppServices.Settings.Notification;
        if (!notification.IsClassOnNotificationEnabled || current is null)
            return;

        var mainWindow = NotificationWindow;
        if (mainWindow is null)
            return;

        var generation = BeginDisplay();
        ShowMask(mainWindow, notification.ClassOnMaskText);
        Schedule(generation, TimeSpan.FromSeconds(3), mainWindow.HideNotificationMask);
        Speak(notification.IsSpeechEnabledOnClassOn, notification.ClassOnMaskText);
    }

    private void ShowClassOff(
        DateTime now,
        (string Title, TimeSpan Start, TimeSpan End)? current,
        TodaySummary summary)
    {
        var notification = AppServices.Settings.Notification;
        if (!notification.IsClassOffNotificationEnabled || current is null)
            return;

        var mainWindow = NotificationWindow;
        if (mainWindow is null)
            return;

        var next = summary.Slots.FirstOrDefault(slot => slot.Start > now.TimeOfDay);
        var generation = BeginDisplay();
        ShowMask(mainWindow, notification.ClassOffMaskText);

        if (next is null)
        {
            Schedule(generation, TimeSpan.FromSeconds(5), mainWindow.HideNotificationMask);
            Speak(notification.IsSpeechEnabledOnClassOff, notification.ClassOffMaskText);
            return;
        }

        var gap = next.Start - current.Value.Start;
        var overlay = string.IsNullOrWhiteSpace(notification.ClassOffOverlayText)
            ? $"本节{notification.ClassOffMaskText}长 {FormatDuration(gap)}"
            : $"{notification.ClassOffOverlayText} · {FormatDuration(gap)}";
        var teacher = notification.ShowTeacherName ? next.Teacher ?? "" : "";
        var speech = $"{overlay}。下节课是{next.DisplayName}" +
                     (teacher.Length > 0 ? $"，由{teacher}老师任教。" : "。");

        Schedule(generation, TimeSpan.FromMilliseconds(1200), () =>
            mainWindow.ShowNotificationOverlay(overlay, next.DisplayName, teacher, next.TimeRange));
        Schedule(generation, TimeSpan.FromSeconds(8), mainWindow.HideNotificationMask);
        Speak(notification.IsSpeechEnabledOnClassOff, speech);
    }

    private MainWindow? NotificationWindow =>
        AppServices.Settings.IsMainWindowVisible &&
        AppServices.MainWindow is { IsEditMode: false } mainWindow
            ? mainWindow
            : null;

    private int BeginDisplay()
    {
        _displayGeneration++;
        return _displayGeneration;
    }

    private void Schedule(int generation, TimeSpan delay, Action action)
    {
        DispatcherTimer.RunOnce(() =>
        {
            if (generation == _displayGeneration)
                action();
        }, delay);
    }

    private void ShowMask(MainWindow mainWindow, string text)
    {
        if (AppServices.Settings.AllowNotificationEffect)
        {
            var center = mainWindow.GetIslandCenterOnScreen() ?? new PixelPoint(0, 0);
            var clipCornerRadius = 14 * Math.Max(0.1, AppServices.Settings.EffectiveScale);
            _effectWindow.PlayEffect(new RippleEffect(
                center,
                CiPalette.SakuraAccentBrush(),
                mainWindow.GetIslandScreenRect,
                clipCornerRadius));
        }

        mainWindow.ShowNotificationMask(text);
    }

    private void Speak(bool enabled, string text)
    {
        if (!enabled || string.IsNullOrWhiteSpace(text) || !OperatingSystem.IsWindows())
            return;

        try
        {
            _speech ??= new SpeechSynthesizer();
            if (_speech.Voice is null)
            {
                var chineseVoice = _speech.GetInstalledVoices()
                    .FirstOrDefault(voice => voice.Enabled &&
                                             voice.VoiceInfo.Culture.Name.StartsWith("zh",
                                                 StringComparison.OrdinalIgnoreCase));
                if (chineseVoice is not null)
                    _speech.SelectVoice(chineseVoice.VoiceInfo.Name);
            }

            _speech.Volume = 100;
            _speech.Rate = 0;
            _speech.SpeakAsyncCancelAll();
            _speech.SpeakAsync(text);
        }
        catch
        {
        }
    }

    public void DebugTriggerClassPreparing()
    {
        var now = DateTime.Now;
        var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        var next = summary.Slots.FirstOrDefault(slot => slot.Start > now.TimeOfDay);
        if (next is not null)
        {
            ShowPreparing(next, next.Start - now.TimeOfDay, AppServices.Settings.Notification);
            return;
        }

        if (summary.Slots.Count > 0)
            ShowPreparing(summary.Slots[0], TimeSpan.FromSeconds(60), AppServices.Settings.Notification);
        else
            ShowPreparing(
                new CourseSlot("演示课程", "演示课程", "", "", now.TimeOfDay + TimeSpan.FromMinutes(1),
                    now.TimeOfDay + TimeSpan.FromMinutes(41)),
                TimeSpan.FromMinutes(1),
                AppServices.Settings.Notification);
    }

    public void DebugTriggerClassOn()
    {
        var now = DateTime.Now;
        var current = FindCurrent(
            AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now).Slots,
            now.TimeOfDay);
        current ??= ("演示课程", now.TimeOfDay, now.TimeOfDay + TimeSpan.FromMinutes(40));
        ShowClassOn(now, current);
    }

    public void DebugTriggerClassOff()
    {
        var now = DateTime.Now;
        var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        var current = FindCurrent(summary.Slots, now.TimeOfDay);
        current ??= ("课间休息", now.TimeOfDay, now.TimeOfDay + TimeSpan.FromMinutes(10));
        ShowClassOff(now, current, summary);
    }

    private string CurrentKey(DateTime now)
    {
        var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        return CurrentKey(summary, now.TimeOfDay, FindCurrent(summary.Slots, now.TimeOfDay));
    }

    private static string CurrentKey(
        TodaySummary summary,
        TimeSpan now,
        (string Title, TimeSpan Start, TimeSpan End)? current)
    {
        if (current is { } c)
            return c.Title == "课间休息" ? $"break:{c.Start}" : $"course:{c.Start}:{c.Title}";

        if (summary.IsFinished)
            return "finished";

        return summary.Slots.Count == 0 ? "empty" : "before";
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalSeconds <= 0)
            return "0 分钟";

        var parts = new List<string>(3);
        if (span.Hours > 0)
            parts.Add($"{span.Hours} 小时");
        if (span.Minutes > 0)
            parts.Add(span.Seconds > 0 ? $"{span.Minutes} 分" : $"{span.Minutes} 分钟");
        if (span.Seconds > 0)
            parts.Add($"{span.Seconds} 秒");

        return string.Join(" ", parts);
    }

    private static (string Title, TimeSpan Start, TimeSpan End)? FindCurrent(
        IReadOnlyList<CourseSlot> slots,
        TimeSpan now)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (i > 0)
            {
                var gapStart = slots[i - 1].End;
                var gapEnd = slots[i].Start;
                if (gapEnd > gapStart && now >= gapStart && now < gapEnd)
                    return ("课间休息", gapStart, gapEnd);
            }

            if (slots[i].Contains(now))
                return (slots[i].Subject, slots[i].Start, slots[i].End);
        }

        return null;
    }
}
