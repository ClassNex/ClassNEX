namespace ClassNex.Models;

/// <summary>编辑课表单元格时定位具体日期、周次与课程对象的引用。</summary>
/// <param name="EnableDay">周一为 1，周日为 7。</param>
/// <param name="Weeks">该课程所在计划的 all、odd 或 even 键。</param>
/// <param name="Schedule">持有此课程的计划对象。</param>
/// <param name="Course">被编辑的课程实例，而非副本。</param>
public sealed record CourseRef(int EnableDay, string Weeks, Schedule Schedule, Course Course)
{
    /// <summary>供中文课程编辑器显示的周次标签。</summary>
    public string WeeksText => Weeks switch
    {
        "odd" => "单周",
        "even" => "双周",
        _ => "每周",
    };
}

/// <summary>周课表中尚未指定科目的目标时间格。</summary>
/// <param name="EnableDay">目标星期，周一为 1。</param>
/// <param name="Start">单元格对应的开始时刻。</param>
/// <param name="End">单元格对应的结束时刻。</param>
public sealed record CellTarget(int EnableDay, string Start, string End);
