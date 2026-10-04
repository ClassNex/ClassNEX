namespace ClassNex.Models;

/// <summary>桌面组件配置（白皮书 Models/WidgetConfig.cs）。</summary>
public sealed class WidgetConfig
{
    /// <summary>组件类型标识，见 <c>WidgetRegistry</c>。</summary>
    public string Type { get; set; } = "";

    /// <summary>是否启用（停用后不显示在主界面）。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>排序（小的在前）。</summary>
    public int Order { get; set; }

    /// <summary>字号缩放。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>自定义文本组件的文本内容，支持 {date} {time} {next} 等占位符。</summary>
    public string? Text { get; set; }

    /// <summary>时钟组件是否显示秒。</summary>
    public bool ShowSeconds { get; set; }
}
