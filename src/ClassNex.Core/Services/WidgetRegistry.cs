namespace ClassNex.Services;

/// <summary>组件库用以展示一种可添加组件的静态信息。</summary>
/// <param name="Type">存储在 WidgetConfig 中的稳定类型键。</param>
/// <param name="DisplayName">在组件选择器中显示的名称。</param>
/// <param name="Description">简短说明该组件向课堂展示什么。</param>
/// <param name="Glyph">供使用相应图标字体的宿主绘制的字形。</param>
public sealed record WidgetTypeInfo(string Type, string DisplayName, string Description, string Glyph);

/// <summary>内置桌面组件类型目录；不持有窗口、控件或组件实例。</summary>
public static class WidgetRegistry
{
    /// <summary>当前可供添加的内置类型及其界面元数据。</summary>
    public static readonly IReadOnlyList<WidgetTypeInfo> Types = new List<WidgetTypeInfo>
    {
        new("text", "文本", "显示自定义文本", "\uE198"),
        new("divider", "分割线", "显示一个分割线，视觉上对组件进行分组", "\uE11C"),
        new("schedule", "课程表", "显示当前的课程表信息", "\uE2B9"),
        new("date", "日期", "显示今天的日期和星期", "\uE063"),
        new("clock", "时间", "显示现在的时间，支持精确到秒", "\uE087"),
        new("nextclass", "当前 / 下节课", "显示正在上的课或下一节课", "\uE049"),
        new("countdown", "倒计时", "显示距离某一天的倒计时", "\uE1E0"),
    };

    /// <summary>通过持久化类型键查找定义；未知键返回 null。</summary>
    public static WidgetTypeInfo? Find(string type) =>
        Types.FirstOrDefault(t => t.Type == type);

    /// <summary>返回类型的显示名；未知类型保留其原始键。</summary>
    public static string DisplayNameOf(string type) =>
        Find(type)?.DisplayName ?? type;

    /// <summary>返回类型说明；未知类型返回空字符串。</summary>
    public static string DescriptionOf(string type) =>
        Find(type)?.Description ?? "";

    /// <summary>返回组件字形，未知类型提供通用后备字形。</summary>
    public static string GlyphOf(string type) =>
        Find(type)?.Glyph ?? "\uE0F9";
}
