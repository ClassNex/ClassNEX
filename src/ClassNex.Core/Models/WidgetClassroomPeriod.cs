using ClassNex.Services;

namespace ClassNex.Models;

/// <summary>单个组件可选的内置显示条件，不是可执行规则。</summary>
public enum WidgetDisplayWhen
{
    /// <summary>不限制显示时段。</summary>
    Always = 0,
    /// <summary>有正在进行的课程。</summary>
    Lesson = 1,
    /// <summary>当前是课间。</summary>
    Break = 2,
    /// <summary>当前有占用时长的活动。</summary>
    Activity = 3,
    /// <summary>该日期没有安排任何课程，包括临时停课。</summary>
    NoLessonsToday = 4
}

/// <summary>组件共用的课堂状态；在下一边界前不必重新判断显示条件。</summary>
/// <param name="Kind">当前课程、课间、活动，或未安排的空闲时段。</param>
/// <param name="NoLessonsToday">当天是否一节课程也没有；有独立活动的日期也可能无课。</param>
/// <param name="Until">下一处可能改变状态的本地时刻，不包含该边界。</param>
public readonly record struct WidgetClassroomPeriod(ClassTimeKind? Kind, bool NoLessonsToday, TimeSpan Until)
{
    /// <summary>未知的未来显示条件按始终显示处理，避免旧客户端意外隐藏组件。</summary>
    public bool Allows(WidgetDisplayWhen condition) => condition switch
    {
        WidgetDisplayWhen.Lesson => Kind == ClassTimeKind.Class,
        WidgetDisplayWhen.Break => Kind == ClassTimeKind.Break,
        WidgetDisplayWhen.Activity => Kind == ClassTimeKind.Action,
        WidgetDisplayWhen.NoLessonsToday => NoLessonsToday,
        _ => true
    };

    /// <summary>选择适用于当天的作息。仅当旧课表没有非课程时段时推断两节课之间的课间。</summary>
    public static WidgetClassroomPeriod At(TodaySummary day, TimeSpan now, IReadOnlyList<TemporaryTimeItem> basePeriods) =>
        At(day, now, basePeriods, out _);

    /// <summary>同时返回当前展示项及它实际的起止时间；下一次状态变化不一定是当前课间的结束。</summary>
    public static WidgetClassroomPeriod At(TodaySummary day, TimeSpan now, IReadOnlyList<TemporaryTimeItem> basePeriods,
        out (string Title, TimeSpan Start, TimeSpan End)? current)
    {
        current = null;
        if (day.Current is { } lesson)
        {
            current = (lesson.Subject, lesson.Start, lesson.End);
            return new(ClassTimeKind.Class, false, lesson.End);
        }
        var until = day.Next?.Start ?? TimeSpan.FromDays(1);
        if (day.IsEmpty && day.IndependentTimeLayout is null) return new(null, true, until);
        IReadOnlyList<TemporaryTimeItem> periods = day.IndependentTimeLayout is { } own ? own : basePeriods;
        TemporaryTimeItem? active = null;
        var hasPeriods = false;
        for (var i = 0; i < periods.Count; i++)
        {
            var item = periods[i];
            if (item.Kind is not (ClassTimeKind.Break or ClassTimeKind.Action) || item.End <= item.Start) continue;
            hasPeriods = true;
            if (item.Start > now) { if (item.Start < until) until = item.Start; continue; }
            if (item.End <= now) continue;
            if (item.End < until) until = item.End;
            if (active is null || item.Kind == ClassTimeKind.Action && active.Kind != ClassTimeKind.Action) active = item;
        }
        if (active != null)
        {
            var title = string.IsNullOrWhiteSpace(active.Name) ? (active.Kind == ClassTimeKind.Action ? "活动" : "课间休息") : active.Name;
            current = (title, active.Start, active.End);
            return new(active.Kind, day.IsEmpty, until);
        }
        if (!hasPeriods && day.IndependentTimeLayout is null && day.Next is { } next)
        {
            TimeSpan? previousEnd = null;
            foreach (var slot in day.Slots)
            {
                if (slot.Start > now) break;
                if (slot.End <= now && (previousEnd == null || slot.End > previousEnd)) previousEnd = slot.End;
            }
            if (previousEnd is { } start)
            {
                current = ("课间休息", start, next.Start);
                return new(ClassTimeKind.Break, false, until);
            }
        }
        return new(null, day.IsEmpty, until);
    }
}
