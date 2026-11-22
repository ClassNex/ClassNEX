namespace ClassNex.Models;

/// <summary>一个桌面组件的持久化实例配置，类型不适用的可选项会被忽略。</summary>
public sealed class WidgetConfig
{
    /// <summary>组件注册表中的稳定类型键，如 schedule。</summary>
    public string Type { get; set; } = "";

    /// <summary>关闭后保留该实例及参数，但不在浮窗中显示。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>在当前桌面布局中的顺序。</summary>
    public int Order { get; set; }

    /// <summary>仅应用于该组件内容的字号倍率。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>文本组件的内容模板，可包含课程或日期占位符。</summary>
    public string? Text { get; set; }

    /// <summary>是否覆盖组件的默认文字颜色。</summary>
    public bool UseCustomFontColor { get; set; }

    /// <summary>启用自定义文字颜色时使用的颜色字符串。</summary>
    public string? FontColor { get; set; }

    /// <summary>是否在时钟中显示秒。</summary>
    public bool ShowSeconds { get; set; }

    /// <summary>保留旧文件中的实时显示偏好；当前时钟直接使用本地时刻。</summary>
    public bool ShowRealTime { get; set; } = true;

    /// <summary>是否让时钟中的分隔符闪动。</summary>
    public bool FlashTimeSeparator { get; set; }

    /// <summary>是否弱化已结束课程的视觉权重。</summary>
    public bool FadeCompletedClasses { get; set; } = true;

    /// <summary>调节课表中相邻科目间距的倍率。</summary>
    public double ScheduleSpacing { get; set; } = 1.0;

    /// <summary>是否显示接下来的课程。</summary>
    public bool ShowNextClass { get; set; } = true;

    /// <summary>自定义倒计时目标名称。</summary>
    public string? CountdownName { get; set; }

    /// <summary>倒计时的目标时间，包含时区偏移。</summary>
    public DateTimeOffset? CountdownTarget { get; set; }

    /// <summary>倒计时组件是否使用强调色。</summary>
    public bool UseCountdownAccent { get; set; } = true;
}
