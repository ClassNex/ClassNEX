using ClassNex.Models;
using System.Runtime.CompilerServices;
using System.Globalization;

namespace ClassNex.Services;

/// <summary>根据本地时刻、单双周和活动档案查询课程，不依赖系统时钟控件。</summary>
public interface ITimeService
{
    /// <summary>按设置的单周起始日和偏移返回 odd 或 even。</summary>
    string GetCurrentParity(DateTime now);

    /// <summary>返回指定星期、周次的时段列表，按开始时间排序，可由调用方独立修改。</summary>
    List<CourseSlot> GetDaySlots(ScheduleProfile profile, int enableDay, string parity);

    /// <summary>以给定本地日期自动确定星期和周次，并返回该日课程。</summary>
    List<CourseSlot> GetTodaySlots(ScheduleProfile profile, DateTime now);

    /// <summary>在给定有序时段中找到当前课程；结束时刻不计入该课程。</summary>
    CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>返回列表中第一节开始时刻严格晚于当前时刻的课。</summary>
    CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>生成人可读的距上课、下课或当天课程已结束的提示。</summary>
    string GetCountdownText(IReadOnlyList<CourseSlot> slots, TimeSpan now);

    /// <summary>在同一个时刻解析日期、周次、当日时段以及当前和下一节课。</summary>
    TodaySummary GetTodaySummary(ScheduleProfile profile, DateTime now);
}

/// <summary>某一本地时刻的课堂快照，供多个桌面组件共享同一轮计算。</summary>
/// <param name="DayNumber">CSES 星期编号，从周一 1 到周日 7。</param>
/// <param name="DayText">用于显示的中文星期标签。</param>
/// <param name="DateText">用于浮窗的简短月日文本。</param>
/// <param name="Parity">计算出的 odd 或 even 周次键。</param>
/// <param name="Slots">当天按开始时间排序的时段副本。</param>
/// <param name="Current">正在进行的授课时段，课间或当天没有课时为 null。</param>
/// <param name="Next">尚未开始的第一节课程，无后续课程时为 null。</param>
public sealed record TodaySummary(
    int DayNumber,
    string DayText,
    string DateText,
    string Parity,
    List<CourseSlot> Slots,
    CourseSlot? Current,
    CourseSlot? Next)
{
    /// <summary>用于显示的中文周次标签。</summary>
    public string ParityText => Parity == "odd" ? "单周" : Parity == "even" ? "双周" : "全周";

    /// <summary>该日没有任何有效的授课时段。</summary>
    public bool IsEmpty => Slots.Count == 0;

    /// <summary>该日有课，但已无正在进行或尚待开始的课程。</summary>
    public bool IsFinished => Slots.Count > 0 && Current is null && Next is null;
}

/// <summary>按档案修订、星期和周次缓存解析后的课表时段；缓存不延长档案生命周期。</summary>
public sealed class TimeService : ITimeService
{
    /// <summary>从周一开始排列的中文简短星期名。</summary>
    public static readonly string[] ChineseDays = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private readonly Func<AppSettings> _settings;
    private readonly ConditionalWeakTable<ScheduleProfile, DayCache> _days = new();

    private sealed class DayCache
    {
        internal long Revision = -1;
        internal readonly Dictionary<(int Day, string Parity), CourseSlot[]> Entries = [];
    }

    /// <summary>使用设置访问器以便下次查询立即反映单双周设置变更。</summary>
    public TimeService(Func<AppSettings> settingsAccessor) => _settings = settingsAccessor;

    /// <summary>把本地日期换算为 CSES 使用的周一 1 到周日 7。</summary>
    public static int DayNumberOf(DateTime date) => ((int)date.DayOfWeek + 6) % 7 + 1;

    /// <inheritdoc />
    public string GetCurrentParity(DateTime now)
    {
        var settings = _settings();
        var start = settings.SingleWeekStartTime.Date;
        var days = (now.Date - start).Days;
        if (days < 0)
            return "odd";

        var weeks = (days / 7) + settings.WeekRotationOffset;
        return weeks % 2 == 0 ? "odd" : "even";
    }

    /// <inheritdoc />
    public List<CourseSlot> GetDaySlots(ScheduleProfile profile, int enableDay, string parity) =>
        new(GetSlots(profile, enableDay, parity));

    /// <summary>Call after editing a standalone profile directly. ScheduleService.Save does this automatically.</summary>
    public void Invalidate(ScheduleProfile profile) => profile.Invalidate();

    private CourseSlot[] GetSlots(ScheduleProfile profile, int enableDay, string parity)
    {
        if (enableDay is < 1 or > 7) return [];
        var key = (enableDay, parity is "odd" or "even" ? parity : "all");
        var cache = _days.GetOrCreateValue(profile);
        lock (cache)
        {
            if (cache.Revision != profile.Revision)
            {
                cache.Entries.Clear();
                cache.Revision = profile.Revision;
            }
            if (!cache.Entries.TryGetValue(key, out var slots))
                cache.Entries[key] = slots = BuildSlots(profile, key.enableDay, key.Item2);
            return slots;
        }
    }

    private static CourseSlot[] BuildSlots(ScheduleProfile profile, int enableDay, string parity)
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
                if (!TimeSpan.TryParse(course.StartTime, CultureInfo.InvariantCulture, out var start))
                    continue;
                if (!TimeSpan.TryParse(course.EndTime, CultureInfo.InvariantCulture, out var end) || start < TimeSpan.Zero || end > TimeSpan.FromDays(1) || end <= start)
                    continue;

                var subject = profile.FindSubject(course.Subject);
                result.Add(new CourseSlot(
                    course.Subject,
                    subject?.DisplayName ?? course.Subject,
                    subject?.Teacher,
                    subject?.Room,
                    start,
                    end,
                    subject?.IsOutDoor ?? false));
            }
        }

        return result.OrderBy(s => s.Start).ToArray();
    }

    /// <inheritdoc />
    public List<CourseSlot> GetTodaySlots(ScheduleProfile profile, DateTime now) =>
        GetDaySlots(profile, DayNumberOf(now), GetCurrentParity(now));

    /// <inheritdoc />
    public CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        foreach (var slot in slots)
        {
            if (slot.Contains(now))
                return slot;
        }

        return null;
    }

    /// <inheritdoc />
    public CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        foreach (var slot in slots)
        {
            if (slot.Start > now)
                return slot;
        }

        return null;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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
