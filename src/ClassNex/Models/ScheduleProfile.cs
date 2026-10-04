using YamlDotNet.Serialization;

namespace ClassNex.Models;

/// <summary>
/// 课表档案（白皮书中的「档案」，对应 CSES 文档根节点）。
/// 一个档案 = 一组科目 + 一份周课表。
/// </summary>
public sealed class ScheduleProfile
{
    /// <summary>CSES 版本号，固定为 1。</summary>
    public int Version { get; set; } = 1;

    /// <summary>名称（本地档案名，不写入 CSES）。</summary>
    [YamlIgnore]
    public string Name { get; set; } = "默认";

    public List<Subject> Subjects { get; set; } = new();

    public List<Schedule> Schedules { get; set; } = new();

    public Subject? FindSubject(string name) =>
        Subjects.FirstOrDefault(s => s.Name == name);

    /// <summary>取某天、某周次的课程（按开始时间排序）。</summary>
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

    /// <summary>找到某天、某周次的 Schedule 对象（没有则创建）。</summary>
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
