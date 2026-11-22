namespace ClassNex.Models;

/// <summary>独立于科目的每日作息模板，可同时包含课时、课间和定点项目。</summary>
public sealed class TimeLayout
{
    /// <summary>编辑器中的时间表名称。</summary>
    public string Name { get; set; } = "默认时间表";

    /// <summary>按布局顺序保存的全部时间项目，不限于课程。</summary>
    public List<ClassTime> Times { get; set; } = new();

    /// <summary>从出现过的课程起止时间生成模板，保留时段间的空隙为课间。</summary>
    public static TimeLayout FromProfile(ScheduleProfile profile)
    {
        var layout = new TimeLayout();
        var seen = new SortedSet<(string Start, string End)>();

        foreach (var schedule in profile.Schedules)
        {
            foreach (var course in schedule.Classes)
                seen.Add((ClassTime.ToShortTime(course.StartTime), ClassTime.ToShortTime(course.EndTime)));
        }

        var index = 1;
        string? previousEnd = null;

        foreach (var (start, end) in seen)
        {
            
            if (previousEnd is not null && TimeSpan.TryParse(previousEnd, out var pe)
                && TimeSpan.TryParse(start, out var cs) && cs > pe)
            {
                layout.Times.Add(new ClassTime
                {
                    Name = "课间",
                    Start = previousEnd,
                    End = start,
                    Kind = ClassTimeKind.Break,
                });
            }

            layout.Times.Add(new ClassTime
            {
                Name = $"第{index++}节",
                Start = start,
                End = end,
                Kind = ClassTimeKind.Class,
            });

            previousEnd = end;
        }

        return layout;
    }

    /// <summary>模板是否尚未包含任何时间项目。</summary>
    public bool IsEmpty => Times.Count == 0;
}
