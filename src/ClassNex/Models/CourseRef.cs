namespace ClassNex.Models;

/// <summary>指向课表中某节课的引用（用于选中 / 编辑 / 删除）。</summary>
public sealed record CourseRef(int EnableDay, string Weeks, Schedule Schedule, Course Course)
{
    public string WeeksText => Weeks switch
    {
        "odd" => "单周",
        "even" => "双周",
        _ => "每周",
    };
}

/// <summary>课表网格中的一个空格（用于按位置新增课程）。</summary>
public sealed record CellTarget(int EnableDay, string Start, string End);
