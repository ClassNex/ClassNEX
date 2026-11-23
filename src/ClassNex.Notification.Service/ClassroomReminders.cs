using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

internal sealed class ClassroomReminders
{
    public const string Prefix = "classnex/lesson/";
    public const string PreparingPrefix = Prefix + "prepare/";
    private (DateOnly Day, string? Course, TimeSpan Edge)? _state;
    private (DateOnly Day, string Course, TimeSpan Start)? _prepared;

    public void Reset() { _state = null; _prepared = null; }

    public void Evaluate(TodaySummary day, AppSettings settings, DateTimeOffset now, Action<NotificationRequest> publish)
    {
        var time = now.TimeOfDay;
        var date = DateOnly.FromDateTime(now.Date);
        var last = day.Slots.LastOrDefault(slot => slot.End <= time);
        var state = (date, day.Current?.Subject, day.Current?.Start ?? last?.End ?? TimeSpan.Zero);
        var changed = _state is { } previous && previous.Day == date && previous != state;
        _state = state;
        if (!settings.AllowNotification) { _prepared = null; return; }

        var options = settings.Notification;
        if (changed)
        {
            if (day.Current is { } current && options.IsClassOnNotificationEnabled && time - current.Start <= TimeSpan.FromSeconds(5))
                publish(ClassOn(now - (time - current.Start), options, $"{Prefix}on/{date.DayNumber}/{current.Start.Ticks}"));
            else if (day.Current is null && last is not null)
            {
                var elapsed = time - last.End;
                var edge = now - elapsed;
                if (day.Next is null && options.IsAfterSchoolNotificationEnabled)
                {
                    if (elapsed <= TimeSpan.FromSeconds(10)) publish(AfterSchool(edge, options, $"{Prefix}end/{date.DayNumber}/{last.End.Ticks}"));
                }
                else if (options.IsClassOffNotificationEnabled && elapsed <= TimeSpan.FromSeconds(5))
                    publish(ClassOff(edge, options, day.Next, last.End, $"{Prefix}off/{date.DayNumber}/{last.End.Ticks}", settings.TimeLayout));
            }
        }

        if (!settings.AllowMinorNotification || !options.IsClassOnPreparingNotificationEnabled ||
            day.Current is not null || day.Next is not { } next) { _prepared = null; return; }
        var threshold = next.IsOutDoor ? options.OutDoorClassPreparingDeltaTime : (double)options.ClassOnBreakReminderMinutes * 60;
        var remaining = next.Start - time;
        if (remaining <= TimeSpan.Zero || remaining.TotalSeconds > threshold) { _prepared = null; return; }
        var key = (date, next.Subject, next.Start);
        if (_prepared == key) return;
        _prepared = key;
        publish(Preparing(now, options, next, remaining, $"{PreparingPrefix}{date.DayNumber}/{next.Start.Ticks}/{next.Subject}"));
    }

    public static NotificationRequest Preparing(DateTimeOffset now, NotificationSettings n, CourseSlot next, TimeSpan remaining, string id)
    {
        var end = now + remaining;
        return new(id, next.IsOutDoor ? n.OutdoorClassOnPreparingMaskText : n.ClassOnPreparingMaskText,
            end, TimeSpan.FromSeconds(3), remaining, NotificationPriority.Low,
            Overlay: new("", next.Subject, n.ShowTeacherName ? next.Teacher ?? "" : "", next.TimeRange, "下节课是",
                next.IsOutDoor ? n.OutdoorClassOnPreparingText : n.ClassOnPreparingText, true, end));
    }

    public static NotificationRequest ClassOn(DateTimeOffset now, NotificationSettings n, string id) =>
        new(id, n.ClassOnMaskText, now.AddSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), NotificationPriority.Important);

    public static NotificationRequest ClassOff(DateTimeOffset now, NotificationSettings n, CourseSlot? next, TimeSpan breakStart, string id, TimeLayout layout)
    {
        if (next is null)
            return new(id, n.ClassOffMaskText, now.AddSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), RightGlyph: "\uE250");
        var period = layout.Times.Find(t => t.Kind == ClassTimeKind.Break && TimeSpan.TryParse(t.Start, out var start) && start == breakStart);
        var end = TimeSpan.TryParse(period?.End, out var periodEnd) && periodEnd > breakStart && periodEnd <= next.Start ? periodEnd : next.Start;
        var span = end - breakStart;
        var parts = new List<string>(3);
        if (span.Hours > 0) parts.Add($"{span.Hours} 小时");
        if (span.Minutes > 0) parts.Add($"{span.Minutes} 分钟");
        if (span.Seconds > 0) parts.Add($"{span.Seconds} 秒");
        var name = string.IsNullOrWhiteSpace(period?.Name) ? "课间" : period.Name;
        var label = $"{name} {(parts.Count == 0 ? "0 分钟" : string.Join(' ', parts))}";
        var duration = TimeSpan.FromSeconds(string.IsNullOrWhiteSpace(n.ClassOffOverlayText) ? 12 : 22);
        return new(id, n.ClassOffMaskText, now + duration, TimeSpan.FromSeconds(2), duration,
            Overlay: new(label, next.Subject, n.ShowTeacherName ? next.Teacher ?? "" : "", next.TimeRange, "下节课是", n.ClassOffOverlayText, true), RightGlyph: "\uE250");
    }

    public static NotificationRequest AfterSchool(DateTimeOffset now, NotificationSettings n, string id) =>
        new(id, "放学", now.AddSeconds(33), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(33),
            Overlay: new("", "", "", "", Message: string.IsNullOrWhiteSpace(n.AfterSchoolText) ? NotificationSettings.DefaultAfterSchoolText : n.AfterSchoolText), RightGlyph: "\uE2C8");

    public static CourseSlot SampleCourse(DateTimeOffset now) =>
        new("演示课程", "演示", null, null, now.TimeOfDay + TimeSpan.FromMinutes(1), now.TimeOfDay + TimeSpan.FromMinutes(41));
}
