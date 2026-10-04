namespace ClassNex.Services;

/// <summary>组件类型信息（供「主界面组件」设置页选择）。</summary>
public sealed record WidgetTypeInfo(string Type, string DisplayName, string Description);

/// <summary>可用的主界面组件类型注册表。</summary>
public static class WidgetRegistry
{
    public static readonly IReadOnlyList<WidgetTypeInfo> Types = new List<WidgetTypeInfo>
    {
        new("date", "日期", "显示今天的星期与日期"),
        new("clock", "时钟", "显示当前时间"),
        new("schedule", "今日课表", "列出今天的全部课程"),
        new("nextclass", "当前 / 下节课", "显示正在上的课或下一节课"),
        new("countdown", "倒计时", "距上课 / 下课的倒计时"),
        new("text", "自定义文本", "自由文本，支持 {date} {time} {day} {next} 占位符"),
    };

    public static WidgetTypeInfo? Find(string type) =>
        Types.FirstOrDefault(t => t.Type == type);

    public static string DisplayNameOf(string type) =>
        Find(type)?.DisplayName ?? type;

    public static string DescriptionOf(string type) =>
        Find(type)?.Description ?? "";
}
