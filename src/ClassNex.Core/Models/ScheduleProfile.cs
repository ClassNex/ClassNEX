using YamlDotNet.Serialization;

namespace ClassNex.Models;

/// <summary>包含科目字典和各日授课计划的整个 CSES 档案。</summary>
public sealed class ScheduleProfile
{
    internal long Revision { get; private set; }

    internal void Invalidate() => Revision++;

    /// <summary>档案声明的 CSES 版本。</summary>
    public int Version { get; set; } = 1;

    /// <summary>本地编辑器使用的档案名，不写入 CSES YAML。</summary>
    [YamlIgnore]
    public string Name { get; set; } = "默认";

    /// <summary>可被课程按名称引用的科目定义。</summary>
    public List<Subject> Subjects { get; set; } = new();

    /// <summary>按星期和周次组合的课程计划。</summary>
    public List<Schedule> Schedules { get; set; } = new();

    /// <summary>按档案中的完整名称查找科目；未找到时返回 null。</summary>
    public Subject? FindSubject(string name) =>
        Subjects.FirstOrDefault(s => s.Name == name);

    /// <summary>合并当天所有符合周次的计划，按开始时间排序。</summary>
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

    /// <summary>找到指定星期、周次的计划，若不存在则在当前档案建立。</summary>
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

    /// <summary>判断计划是否适用于查询周次；未指定 odd 或 even 时不限周次。</summary>
    public static bool MatchesParity(string weeks, string parity) => parity switch
    {
        "odd" => weeks == "all" || weeks == "odd",
        "even" => weeks == "all" || weeks == "even",
        _ => true,
    };
}
