namespace ClassNex.Models;


public sealed class TimeLayout
{
    public string Name { get; set; } = "默认时间表";

    public List<ClassTime> Times { get; set; } = new();

    
    
    
    
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

    
    public bool IsEmpty => Times.Count == 0;
}
