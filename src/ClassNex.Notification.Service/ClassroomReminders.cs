using System.Globalization;
using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

internal sealed class ClassroomReminders
{
    public const string Prefix = "classnex/lesson/";
    public const string PreparingPrefix = Prefix + "prepare/";
    private static readonly string DefaultBreakTitle = new NotificationSettings().ClassOffMaskText;
    private (DateOnly Day, string? Course, TimeSpan Edge, TimeSpan ObservedAt)? _state;
    private (DateOnly Day, string Course, TimeSpan Start)? _prepared;

    public void Reset() { _state = null; _prepared = null; }

    public void Evaluate(TodaySummary day, AppSettings settings, DateTimeOffset now, TimeSpan? dismissal, Action<NotificationRequest> publish)
    {
        var time = now.TimeOfDay;
        var date = DateOnly.FromDateTime(now.Date);
        var last = day.Slots.LastOrDefault(slot => slot.End <= time);
        var edge = day.Current?.Start ?? last?.End ?? TimeSpan.Zero;
        var previous = _state;
        _state = (date, day.Current?.Subject, edge, time);
        if (!settings.AllowNotification) { _prepared = null; return; }

        var options = settings.Notification;
        if (previous is { } old && old.Day == date)
        {
            var changed = old.Course != day.Current?.Subject || old.Edge != edge;
            if (changed && day.Current is { } current && options.IsClassOnNotificationEnabled && time - current.Start <= TimeSpan.FromSeconds(5))
                publish(ClassOn(now - (time - current.Start), options, $"{Prefix}on/{date.DayNumber}/{current.Start.Ticks}"));
            else if (changed && day.Current is null && last is not null && options.IsClassOffNotificationEnabled &&
                (dismissal is null || time < dismissal) && time - last.End <= TimeSpan.FromSeconds(5))
            {
                var at = now - (time - last.End);
                var request = ClassOff(at, options, day.Next, last.End, $"{Prefix}off/{date.DayNumber}/{last.End.Ticks}", settings.TimeLayout);
                if (dismissal is { } finish && finish - last.End < request.Duration)
                {
                    var duration = finish - last.End;
                    request = request with { Duration = duration, MaskDuration = duration, ExpiresAt = at + duration };
                }
                publish(request);
            }
            if (dismissal is { } end && old.ObservedAt < end && end <= time && time - end <= TimeSpan.FromSeconds(10))
                publish(AfterSchool(now - (time - end), options, $"{Prefix}end/{date.DayNumber}/{end.Ticks}"));
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

    public static TimeSpan? DismissalTime(TodaySummary day, TimeLayout layout)
    {
        if (day.IsEmpty) return null;
        var lastLesson = day.Slots.Max(s => s.End);
        var endOfDay = lastLesson;
        foreach (var item in layout.Times)
        {
            if (item.Kind is not (ClassTimeKind.Class or ClassTimeKind.Break) || !TryPeriod(item, out var start, out var end) || start < lastLesson) continue;
            if (item.Kind == ClassTimeKind.Class) return lastLesson;
            if (end > endOfDay) endOfDay = end;
        }
        return endOfDay;
    }

    private static bool TryPeriod(ClassTime item, out TimeSpan start, out TimeSpan end)
    {
        end = default;
        return TimeSpan.TryParse(item.Start, CultureInfo.InvariantCulture, out start) && TimeSpan.TryParse(item.End, CultureInfo.InvariantCulture, out end) &&
            start >= TimeSpan.Zero && end > start && end <= TimeSpan.FromDays(1);
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
        var period = layout.Times.Find(t => t.Kind == ClassTimeKind.Break && TryPeriod(t, out var start, out var itemEnd) && start == breakStart && (next is null || itemEnd <= next.Start));
        var breakName = string.IsNullOrWhiteSpace(period?.Name) ? null : period.Name.Trim();
        var mask = !string.IsNullOrWhiteSpace(n.ClassOffMaskText) && n.ClassOffMaskText.Trim() != DefaultBreakTitle
            ? n.ClassOffMaskText : breakName is null or "课间" ? DefaultBreakTitle : breakName;
        if (next is null)
            return new(id, mask, now.AddSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), RightGlyph: "\uE250");
        var end = period is not null && TryPeriod(period, out _, out var periodEnd) ? periodEnd : next.Start;
        var span = end - breakStart;
        var parts = new List<string>(3);
        if (span.Hours > 0) parts.Add($"{span.Hours} 小时");
        if (span.Minutes > 0) parts.Add($"{span.Minutes} 分钟");
        if (span.Seconds > 0) parts.Add($"{span.Seconds} 秒");
        var label = $"{breakName ?? "课间"} {(parts.Count == 0 ? "0 分钟" : string.Join(' ', parts))}";
        var duration = TimeSpan.FromSeconds(string.IsNullOrWhiteSpace(n.ClassOffOverlayText) ? 12 : 22);
        return new(id, mask, now + duration, TimeSpan.FromSeconds(2), duration,
            Overlay: new(label, next.Subject, n.ShowTeacherName ? next.Teacher ?? "" : "", next.TimeRange, "下节课是", n.ClassOffOverlayText, true), RightGlyph: "\uE250");
    }

    public static NotificationRequest AfterSchool(DateTimeOffset now, NotificationSettings n, string id) =>
        new(id, "放学", now.AddSeconds(33), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(33),
            Overlay: new("", "", "", "", Message: string.IsNullOrWhiteSpace(n.AfterSchoolText) ? NotificationSettings.DefaultAfterSchoolText : n.AfterSchoolText), RightGlyph: "\uE2C8");

    public static CourseSlot SampleCourse(DateTimeOffset now) =>
        new("演示课程", "演示", null, null, now.TimeOfDay + TimeSpan.FromMinutes(1), now.TimeOfDay + TimeSpan.FromMinutes(41));
}
