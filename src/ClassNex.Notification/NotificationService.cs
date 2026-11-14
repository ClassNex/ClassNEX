using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed class NotificationService : INotificationService, IDisposable
{
    private readonly INotificationSurface _surface;
    private readonly Func<AppSettings> _settings;
    private readonly ITimeService _time;
    private readonly IScheduleService _schedule;
    private Timer? _timer;

    private string _lastKey = "\0";
    private string? _prepareKey;
    private bool _started;
    private int _displayGeneration;
    private DateTime? _prepareCountdownTarget;

    public NotificationService(
        INotificationSurface surface,
        Func<AppSettings> settings,
        ITimeService time,
        IScheduleService schedule)
    {
        _surface = surface;
        _settings = settings;
        _time = time;
        _schedule = schedule;
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _lastKey = CurrentKey(DateTime.Now);
        _timer ??= new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
        _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _started = false;
        _prepareKey = null;
        _prepareCountdownTarget = null;
    }

    private void OnTick()
    {
        if (!_settings().AllowNotification)
            return;

        UpdatePrepareCountdown();

        var now = DateTime.Now;
        var summary = _time.GetTodaySummary(_schedule.Profile, now);
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
        var settings = _settings();
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
            : notification.ClassOnBreakReminderMinutes * 60;
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
        if (!_surface.IsAvailable)
            return;

        var mask = next.IsOutDoor
            ? notification.OutdoorClassOnPreparingMaskText
            : notification.ClassOnPreparingMaskText;
        var message = next.IsOutDoor
            ? notification.OutdoorClassOnPreparingText
            : notification.ClassOnPreparingText;
        var teacher = notification.ShowTeacherName ? next.Teacher ?? "" : "";
        var generation = BeginDisplay();
        var countdownTarget = DateTime.Now.Add(remaining);
        _prepareCountdownTarget = countdownTarget;
        ShowMask(mask, "\uE0FF", "\uE54F");
        var maskDuration = TimeSpan.FromSeconds(3);
        var overlayDelay = remaining > maskDuration ? maskDuration : remaining;
        var hideDelay = remaining > maskDuration ? remaining : maskDuration;
        Schedule(generation, overlayDelay, () =>
        {
            var countdown = countdownTarget - DateTime.Now;
            if (countdown < TimeSpan.Zero)
                countdown = TimeSpan.Zero;
            _surface.ShowOverlay(
                FormatPrepareCountdown(countdown),
                next.DisplayName,
                teacher,
                next.TimeRange,
                "下节课是：",
                message,
                true);
        });
        Schedule(generation, hideDelay, _surface.Hide);
    }

    private void ShowClassOn(DateTime now, (string Title, TimeSpan Start, TimeSpan End)? current)
    {
        var notification = _settings().Notification;
        if (!notification.IsClassOnNotificationEnabled || current is null)
            return;

        if (!_surface.IsAvailable)
            return;

        var generation = BeginDisplay();
        ShowMask(notification.ClassOnMaskText, "\uE0FF", "\uE54F");
        Schedule(generation, TimeSpan.FromSeconds(5), _surface.Hide);
    }

    private void ShowClassOff(
        DateTime now,
        (string Title, TimeSpan Start, TimeSpan End)? current,
        TodaySummary summary)
    {
        var notification = _settings().Notification;
        if (!notification.IsClassOffNotificationEnabled || current is null)
            return;

        if (!_surface.IsAvailable)
            return;

        var next = summary.Slots.FirstOrDefault(slot => slot.Start > now.TimeOfDay);
        var generation = BeginDisplay();
        ShowMask(notification.ClassOffMaskText, "\uE0FF", "\uE250");

        if (next is null)
        {
            Schedule(generation, TimeSpan.FromSeconds(5), _surface.Hide);
            return;
        }

        var gap = next.Start - current.Value.Start;
        var overlay = string.IsNullOrWhiteSpace(notification.ClassOffOverlayText)
            ? $"本节{notification.ClassOffMaskText}长 {FormatDuration(gap)}"
            : $"{notification.ClassOffOverlayText} · {FormatDuration(gap)}";
        var teacher = notification.ShowTeacherName ? next.Teacher ?? "" : "";
        Schedule(generation, TimeSpan.FromMilliseconds(1200), () =>
            _surface.ShowOverlay(overlay, next.DisplayName, teacher, next.TimeRange, "下节课是：", "", false));
        Schedule(generation, TimeSpan.FromSeconds(8), _surface.Hide);
    }

    private int BeginDisplay()
    {
        _prepareCountdownTarget = null;
        _displayGeneration++;
        return _displayGeneration;
    }

    private void UpdatePrepareCountdown()
    {
        if (_prepareCountdownTarget is not { } target)
            return;

        var remaining = target - DateTime.Now;
        if (remaining <= TimeSpan.Zero)
        {
            _prepareCountdownTarget = null;
            return;
        }

        _surface.UpdateCountdown(FormatPrepareCountdown(remaining));
    }

    private static string FormatPrepareCountdown(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"距上课还剩 {seconds} 秒";
    }

    private async void Schedule(int generation, TimeSpan delay, Action action)
    {
        await Task.Delay(delay);
        if (generation == _displayGeneration)
            action();
    }

    private void ShowMask(
        string text,
        string leftGlyph = "\uE0FF",
        string rightGlyph = "\uE54F")
    {
        _surface.ShowMask(text, leftGlyph, rightGlyph, _settings().AllowNotificationEffect);
    }

    public void DebugTriggerClassPreparing()
    {
        var now = DateTime.Now;
        var summary = _time.GetTodaySummary(_schedule.Profile, now);
        var next = summary.Slots.FirstOrDefault(slot => slot.Start > now.TimeOfDay);
        if (next is not null)
        {
            ShowPreparing(next, next.Start - now.TimeOfDay, _settings().Notification);
            return;
        }

        if (summary.Slots.Count > 0)
            ShowPreparing(summary.Slots[0], TimeSpan.FromSeconds(60), _settings().Notification);
        else
            ShowPreparing(
                new CourseSlot("演示课程", "演示课程", "", "", now.TimeOfDay + TimeSpan.FromMinutes(1),
                    now.TimeOfDay + TimeSpan.FromMinutes(41)),
                TimeSpan.FromMinutes(1),
                _settings().Notification);
    }

    public void DebugTriggerClassOn()
    {
        var now = DateTime.Now;
        var current = FindCurrent(
            _time.GetTodaySummary(_schedule.Profile, now).Slots,
            now.TimeOfDay);
        current ??= ("演示课程", now.TimeOfDay, now.TimeOfDay + TimeSpan.FromMinutes(40));
        ShowClassOn(now, current);
    }

    public void DebugTriggerClassOff()
    {
        var now = DateTime.Now;
        var summary = _time.GetTodaySummary(_schedule.Profile, now);
        var current = FindCurrent(summary.Slots, now.TimeOfDay);
        current ??= ("课间休息", now.TimeOfDay, now.TimeOfDay + TimeSpan.FromMinutes(10));
        ShowClassOff(now, current, summary);
    }

    public void DebugTriggerTestNotification()
    {
        if (!_surface.IsAvailable)
            return;

        var generation = BeginDisplay();
        ShowMask("测试提醒", "\uE0FF", "\uE54F");
        Schedule(generation, TimeSpan.FromMilliseconds(1200), () =>
            _surface.ShowOverlay(
                "这是一条测试提醒",
                "通知系统工作正常",
                "",
                DateTime.Now.ToString("HH:mm"),
                "",
                "",
                false));
        Schedule(generation, TimeSpan.FromSeconds(8), _surface.Hide);
    }

    private string CurrentKey(DateTime now)
    {
        var summary = _time.GetTodaySummary(_schedule.Profile, now);
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

    public void Dispose()
    {
        Stop();
        _timer?.Dispose();
        _timer = null;
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
