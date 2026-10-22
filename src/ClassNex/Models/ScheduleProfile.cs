using YamlDotNet.Serialization;

namespace ClassNex.Models;





public sealed class ScheduleProfile
{
    
    public int Version { get; set; } = 1;

    
    [YamlIgnore]
    public string Name { get; set; } = "默认";

    public List<Subject> Subjects { get; set; } = new();

    public List<Schedule> Schedules { get; set; } = new();

    public Subject? FindSubject(string name) =>
        Subjects.FirstOrDefault(s => s.Name == name);

    
    public List<Course> GetCourses(int enableDay, string parity)
    {
        var list = new List<Course>();
        foreach (var schedule in Schedules)
        {
            if (schedule.EnableDay != enableDay)
                continue;
            if (!MatchesParity(schedule.Weeks, parity))
                continue;
            list.AddRange(schedule.Classes);
        }

        return list
            .OrderBy(c => TimeSpan.TryParse(c.StartTime, out var t) ? t : TimeSpan.MaxValue)
            .ToList();
    }

    
    public Schedule GetOrCreateSchedule(int enableDay, string weeks)
    {
        var found = Schedules.FirstOrDefault(s => s.EnableDay == enableDay && s.Weeks == weeks);
        if (found is not null)
            return found;

        found = new Schedule
        {
            Name = $"星期{ScheduleCalculatorDayNames[enableDay - 1]}",
            EnableDay = enableDay,
            Weeks = weeks,
        };
        Schedules.Add(found);
        return found;
    }

    private static readonly string[] ScheduleCalculatorDayNames =
        { "一", "二", "三", "四", "五", "六", "日" };

    public static bool MatchesParity(string weeks, string parity) => parity switch
    {
        "odd" => weeks == "all" || weeks == "odd",
        "even" => weeks == "all" || weeks == "even",
        _ => true,
    };
}
