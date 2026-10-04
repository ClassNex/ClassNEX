namespace ClassNex.Models;

/// <summary>时间表 / 时间轴布局（白皮书 Models/TimeLayout.cs）：有序的节次集合。</summary>
public sealed class TimeLayout
{
    public string Name { get; set; } = "默认时间表";

    public List<ClassTime> Times { get; set; } = new();

    /// <summary>从课表档案反推时间表：取所有课程出现过的起止时间为节次。</summary>
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
        foreach (var (start, end) in seen)
            layout.Times.Add(new ClassTime { Name = $"第{index++}节", Start = start, End = end });

        return layout;
    }

    /// <summary>时间表是否为空。</summary>
    public bool IsEmpty => Times.Count == 0;
}
