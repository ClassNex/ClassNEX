namespace ClassNex.Models;

/// <summary>节次类型：上课 / 课间。</summary>
public enum ClassTimeKind
{
    Class = 0,
    Break = 1,
}

/// <summary>一格作息时间（白皮书 Models/ClassTime.cs）：节次名 + 起止时间。</summary>
public sealed class ClassTime
{
    /// <summary>节次名，如「第1节」「上午第一节」。</summary>
    public string Name { get; set; } = "";

    /// <summary>开始时间，格式 HH:mm。</summary>
    public string Start { get; set; } = "08:00";

    /// <summary>结束时间，格式 HH:mm。</summary>
    public string End { get; set; } = "08:45";

    public ClassTimeKind Kind { get; set; } = ClassTimeKind.Class;

    public string Display => $"{Name}  {Start}–{End}";

    /// <summary>把 HH:mm 转成 CSES 的 HH:mm:ss。</summary>
    public static string ToCsesTime(string shortTime) =>
        shortTime.Length >= 8 ? shortTime : (shortTime.Length == 5 ? shortTime + ":00" : shortTime);

    /// <summary>把 CSES 的 HH:mm:ss 转成 HH:mm。</summary>
    public static string ToShortTime(string csesTime) =>
        csesTime.Length >= 5 ? csesTime[..5] : csesTime;

    /// <summary>判断两个时间是否表示同一时刻（忽略秒）。</summary>
    public static bool SameTime(string a, string b) =>
        TimeSpan.TryParse(a, out var ta) && TimeSpan.TryParse(b, out var tb)
            ? ta.Hours == tb.Hours && ta.Minutes == tb.Minutes
            : string.Equals(a, b, StringComparison.Ordinal);
}
