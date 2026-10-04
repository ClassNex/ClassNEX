namespace ClassNex.Models;

/// <summary>一节课的运行时视图（课程 + 解析后的时间），供组件与时间服务使用。</summary>
public sealed record CourseSlot(
    string Subject,
    string DisplayName,
    string? Teacher,
    string? Room,
    TimeSpan Start,
    TimeSpan End)
{
    public string StartText => Start.ToString(@"hh\:mm");

    public string EndText => End.ToString(@"hh\:mm");

    public string TimeRange => $"{StartText}–{EndText}";

    /// <summary>教师 · 教室（缺失则返回空串）。</summary>
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

    /// <summary>是否正在进行中。</summary>
    public bool Contains(TimeSpan time) => Start <= time && time < End;
}
