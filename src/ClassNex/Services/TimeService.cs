using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 时间服务（白皮书 Core/Services/ITimeService.cs）：
/// 当前节次计算、倒计时、今日课程。
/// </summary>
public interface ITimeService
{
    /// <summary>按「单周起始日期」推算当前周次类型：odd / even。</summary>
    string GetCurrentParity(DateTime now);

    /// <summary>取某天（1=周一 ... 7=周日）在当前周次下的课程。</summary>
    List<CourseSlot> GetDaySlots(ScheduleProfile profile, int enableDay, string parity);

    /// <summary>取今日课程（已按开始时间排序）。</summary>
    List<CourseSlot> GetTodaySlots(ScheduleProfile profile, DateTime now);

    /// <summary>当前正在上的课。</summary>
    CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>下一节课。</summary>
    CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>倒计时文本，如「12 分钟后」「即将上课」。</summary>
    string GetCountdownText(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>今日课表摘要（供主界面显示）。</summary>
    TodaySummary GetTodaySummary(ScheduleProfile profile, DateTime now);
}

/// <summary>今日课表摘要。</summary>
public sealed record TodaySummary(
    int DayNumber,
    string DayText,
    string DateText,
    string Parity,
    List<CourseSlot> Slots,
    CourseSlot? Current,
    CourseSlot? Next)
{
    public string ParityText => Parity == "odd" ? "单周" : Parity == "even" ? "双周" : "全周";

    /// <summary>没有课。</summary>
    public bool IsEmpty => Slots.Count == 0;

    /// <summary>今日课程已全部结束。</summary>
    public bool IsFinished => Slots.Count > 0 && Current is null && Next is null;
}

public sealed class TimeService : ITimeService
{
    public static readonly string[] ChineseDays = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private readonly Func<AppSettings> _settings;

    public TimeService(Func<AppSettings> settingsAccessor) => _settings = settingsAccessor;

    /// <summary>CSES 的 enable_day：1=周一 ... 7=周日。</summary>
    public static int DayNumberOf(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    public string GetCurrentParity(DateTime now)
    {
        var start = _settings().SingleWeekStartTime.Date;
        var days = (now.Date - start).Days;
        if (days < 0)
            return "odd";

        var weeks = (days / 7) + _settings().WeekRotationOffset;
        return weeks % 2 == 0 ? "odd" : "even";
    }

    public List<CourseSlot> GetDaySlots(ScheduleProfile profile, int enableDay, string parity)
    {
        var result = new List<CourseSlot>();
        foreach (var schedule in profile.Schedules)
        {
            if (schedule.EnableDay != enableDay)
                continue;
            if (!ScheduleProfile.MatchesParity(schedule.Weeks, parity))
                continue;

            foreach (var course in schedule.Classes)
            {
                if (!TimeSpan.TryParse(course.StartTime, out var start))
                    continue;
                if (!TimeSpan.TryParse(course.EndTime, out var end))
                    continue;

                var subject = profile.FindSubject(course.Subject);
                result.Add(new CourseSlot(
                    course.Subject,
                    subject?.DisplayName ?? course.Subject,
                    subject?.Teacher,
                    subject?.Room,
                    start,
                    end));
            }
        }

        return result.OrderBy(s => s.Start).ToList();
    }

    public List<CourseSlot> GetTodaySlots(ScheduleProfile profile, DateTime now) =>
        GetDaySlots(profile, DayNumberOf(now), GetCurrentParity(now));

    public CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        foreach (var slot in slots)
        {
            if (slot.Contains(now))
                return slot;
        }

        return null;
    }

    public CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        foreach (var slot in slots)
        {
            if (slot.Start > now)
                return slot;
        }

        return null;
    }

    public string GetCountdownText(IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        if (GetCurrentCourse(slots, now) is { } current)
        {
            var left = current.End - now;
            return left.TotalMinutes < 1
                ? "即将下课"
                : $"距下课 {Format(left)}";
        }

        if (GetNextCourse(slots, now) is { } next)
        {
            var left = next.Start - now;
            return left.TotalMinutes < 1
                ? "即将上课"
                : $"距上课 {Format(left)}";
        }

        return slots.Count == 0 ? "今天没有课程。" : "今日课程已结束";
    }

    public TodaySummary GetTodaySummary(ScheduleProfile profile, DateTime now)
    {
        var day = DayNumberOf(now);
        var parity = GetCurrentParity(now);
        var slots = GetDaySlots(profile, day, parity);

        return new TodaySummary(
            day,
            ChineseDays[day - 1],
            $"{now.Month}/{now.Day:00}",
            parity,
            slots,
            GetCurrentCourse(slots, now.TimeOfDay),
            GetNextCourse(slots, now.TimeOfDay));
    }

    private static string Format(TimeSpan span)
    {
        if (span.TotalMinutes >= 60)
            return $"{(int)span.TotalHours} 小时 {span.Minutes} 分钟";
        return $"{(int)Math.Ceiling(span.TotalMinutes)} 分钟";
    }
}
