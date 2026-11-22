namespace ClassNex.Models;

/// <summary>时间表项目的类别；数值也是历史设置文件中的持久化值。</summary>
public enum ClassTimeKind
{
    /// <summary>可在课表中安排科目的时段。</summary>
    Class = 0,
    /// <summary>没有授课科目的课间时段。</summary>
    Break = 1,
    /// <summary>用于分隔时间表区域的零时长标记。</summary>
    Separator = 2,
    /// <summary>时间表中的定点行动。</summary>
    Action = 3,
}

/// <summary>每日时间表的一个时段或定点项目，独立于具体课程科目。</summary>
public sealed class ClassTime
{
    /// <summary>在编辑器和时间表中显示的项目名称。</summary>
    public string Name { get; set; } = "";

    /// <summary>项目开始的本地时刻，通常为 HH:mm。</summary>
    public string Start { get; set; } = "08:00";

    /// <summary>项目结束的本地时刻；定点项目通常与开始时刻相同。</summary>
    public string End { get; set; } = "08:45";

    /// <summary>区分课程、课间、分隔点和行动。</summary>
    public ClassTimeKind Kind { get; set; } = ClassTimeKind.Class;

    /// <summary>供选择列表使用的名称和时间范围。</summary>
    public string Display => $"{Name}  {Start}–{End}";

    /// <summary>将五字符的 HH:mm 补为 CSES 的 HH:mm:ss，其他输入原样保留。</summary>
    public static string ToCsesTime(string shortTime) =>
        shortTime.Length >= 8 ? shortTime : (shortTime.Length == 5 ? shortTime + ":00" : shortTime);

    /// <summary>将 CSES 时刻取到分钟，供时间表编辑器使用。</summary>
    public static string ToShortTime(string csesTime) =>
        csesTime.Length >= 5 ? csesTime[..5] : csesTime;

    /// <summary>按小时和分钟比较两个时刻；解析失败时退回原字符串比较。</summary>
    public static bool SameTime(string a, string b) =>
        TimeSpan.TryParse(a, out var ta) && TimeSpan.TryParse(b, out var tb)
            ? ta.Hours == tb.Hours && ta.Minutes == tb.Minutes
            : string.Equals(a, b, StringComparison.Ordinal);
}
