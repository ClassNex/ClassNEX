namespace ClassNex.Services;

/// <summary>组件类型信息（供「主界面组件」设置页选择）。</summary>
public sealed record WidgetTypeInfo(string Type, string DisplayName, string Description);

/// <summary>可用的主界面组件类型注册表。</summary>
public static class WidgetRegistry
{
    public static readonly IReadOnlyList<WidgetTypeInfo> Types = new List<WidgetTypeInfo>
    {
        new("text", "文本", "显示自定义文本"),
        new("divider", "分割线", "显示一个分割线，视觉上对组件进行分组"),
        new("schedule", "课程表", "显示当前的课程表信息"),
        new("date", "日期", "显示今天的日期和星期"),
        new("clock", "时钟", "显示现在的时间，支持精确到秒"),
        new("nextclass", "当前 / 下节课", "显示正在上的课或下一节课"),
        new("countdown", "倒计时", "显示距离某一天的倒计时"),
    };

    public static WidgetTypeInfo? Find(string type) =>
        Types.FirstOrDefault(t => t.Type == type);

    public static string DisplayNameOf(string type) =>
        Find(type)?.DisplayName ?? type;

    public static string DescriptionOf(string type) =>
        Find(type)?.Description ?? "";
}
