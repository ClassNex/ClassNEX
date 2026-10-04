namespace ClassNex.Models;

/// <summary>时间表 / 时间轴布局（白皮书 Models/TimeLayout.cs）：有序的节次集合。</summary>
public sealed class TimeLayout
{
    public string Name { get; set; } = "默认时间表";

    public List<ClassTime> Times { get; set; } = new();

    /// <summary>
    /// 从课表档案反推时间表：取所有课程出现过的起止时间为「上课」时间点，
    /// 相邻时间点之间的空档自动补成「课间」时间点（对齐 CI 时间表的形态）。
    /// </summary>
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
            // 上一个时间点结束到本时间点开始之间的空档 = 课间
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

    /// <summary>时间表是否为空。</summary>
    public bool IsEmpty => Times.Count == 0;
}
