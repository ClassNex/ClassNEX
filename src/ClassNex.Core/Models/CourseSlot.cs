namespace ClassNex.Models;

/// <summary>为某一天解析好的不可变授课时段，查询时无需再次解析 YAML。</summary>
/// <param name="Subject">档案中引用的完整科目名。</param>
/// <param name="DisplayName">科目简称；没有简称时为完整科目名。</param>
/// <param name="Teacher">可选教师姓名。</param>
/// <param name="Room">可选上课地点。</param>
/// <param name="Start">从本地午夜算起的开始时间，包含该时刻。</param>
/// <param name="End">从本地午夜算起的结束时间，不包含该时刻。</param>
/// <param name="IsOutDoor">是否采用户外课程的课前提醒提前量。</param>
public sealed record CourseSlot(
    string Subject,
    string DisplayName,
    string? Teacher,
    string? Room,
    TimeSpan Start,
    TimeSpan End,
    bool IsOutDoor = false)
{
    /// <summary>不含秒的开始时刻。</summary>
    public string StartText => Start.ToString(@"hh\:mm");

    /// <summary>不含秒的结束时刻。</summary>
    public string EndText => End.ToString(@"hh\:mm");

    /// <summary>由起止时刻组合的简短标签。</summary>
    public string TimeRange => $"{StartText}–{EndText}";

    /// <summary>合并非空教师和地点；均为空时返回空字符串。</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Teacher))
                parts.Add(Teacher!);
            if (!string.IsNullOrWhiteSpace(Room))
                parts.Add(Room!);
            return string.Join(" · ", parts);
        }
    }

    /// <summary>判断时刻是否落在左闭右开区间 [Start, End) 内。</summary>
    public bool Contains(TimeSpan time) => Start <= time && time < End;
}
