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
            if (day.Current is { } current && options.IsClassOnNotificationEnabled)
                publish(ClassOn(now - (time - current.Start), options, $"{Prefix}on/{date.DayNumber}/{current.Start.Ticks}"));
            else if (day.Current is null && last is not null && options.IsClassOffNotificationEnabled)
                publish(ClassOff(now - (time - last.End), options, day.Next, last.End, $"{Prefix}off/{date.DayNumber}/{last.End.Ticks}"));
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
            Overlay: new("", next.Subject, n.ShowTeacherName ? next.Teacher ?? "" : "", next.TimeRange, "下节课",
                next.IsOutDoor ? n.OutdoorClassOnPreparingText : n.ClassOnPreparingText, true, end));
    }

    public static NotificationRequest ClassOn(DateTimeOffset now, NotificationSettings n, string id) =>
        new(id, n.ClassOnMaskText, now.AddSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), NotificationPriority.Important);

    public static NotificationRequest ClassOff(DateTimeOffset now, NotificationSettings n, CourseSlot? next, TimeSpan breakStart, string id)
    {
        var span = next?.Start - breakStart;
        var duration = span is null ? "" : span.Value.TotalSeconds < 60 ? $"{(int)span.Value.TotalSeconds} 秒" : $"{(int)span.Value.TotalMinutes} 分钟";
        var label = string.IsNullOrWhiteSpace(n.ClassOffOverlayText) ? $"课间 {duration}" : $"{n.ClassOffOverlayText} · {duration}";
        return new(id, n.ClassOffMaskText, now.AddSeconds(8), TimeSpan.FromMilliseconds(1200), TimeSpan.FromSeconds(8),
            Overlay: next is null ? null : new(label, next.Subject, n.ShowTeacherName ? next.Teacher ?? "" : "", next.TimeRange, "下节课"), RightGlyph: "\uE250");
    }

    public static CourseSlot SampleCourse(DateTimeOffset now) =>
        new("演示课程", "演示", null, null, now.TimeOfDay + TimeSpan.FromMinutes(1), now.TimeOfDay + TimeSpan.FromMinutes(41));
}
