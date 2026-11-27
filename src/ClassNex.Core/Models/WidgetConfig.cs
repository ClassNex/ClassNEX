namespace ClassNex.Models;

/// <summary>一个桌面组件的持久化实例配置，类型不适用的可选项会被忽略。</summary>
public sealed class WidgetConfig
{
    /// <summary>复制此组件的设置，包括独立的外观间距。</summary>
    public WidgetConfig Copy()
    {
        var instance = (WidgetConfig)MemberwiseClone();
        if (Presentation is { } style) instance.Presentation = style with { Margin = style.Margin?.ToArray() };
        return instance;
    }

    /// <summary>组件注册表中的稳定类型键，如 schedule。</summary>
    public string Type { get; set; } = "";

    /// <summary>关闭后保留该实例及参数，但不在浮窗中显示。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>将已启用的实例限制在指定课堂时段；旧配置默认始终显示。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public WidgetDisplayWhen DisplayWhen { get; set; }

    /// <summary>在当前桌面布局中的顺序。</summary>
    public int Order { get; set; }

    /// <summary>从零开始的桌面行号；旧版单行配置默认为第一行。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int Row { get; set; }

    /// <summary>先按行、再按行内顺序枚举，不改变原有实例。</summary>
    public static IOrderedEnumerable<WidgetConfig> InDisplayOrder(IEnumerable<WidgetConfig> widgets) =>
        widgets.OrderBy(w => w.Row).ThenBy(w => w.Order);

    /// <summary>将一个实例放到已有行的末尾，传入空行号则在最后新建一行。返回新的显示序号，目标已失效时返回 -1。</summary>
    public static int MoveToRowEnd(List<WidgetConfig> widgets, WidgetConfig instance, int? destination)
    {
        if (!widgets.Contains(instance) || destination is int id && !widgets.Any(w => w.Row == id)) return -1;
        var ordered = InDisplayOrder(widgets).ToList();
        if (destination == instance.Row || destination is null && ordered[^1] == instance && ordered.Count(w => w.Row == instance.Row) == 1)
            return ordered.IndexOf(instance);
        if (destination is null && ordered[^1].Row == int.MaxValue)
        {
            var rows = ordered.GroupBy(w => w.Row).ToArray();
            for (var r = 0; r < rows.Length; r++) foreach (var w in rows[r]) w.Row = r;
        }
        var row = destination ?? (ordered[^1].Row + 1);
        ordered.Remove(instance);
        var at = ordered.FindLastIndex(w => w.Row <= row) + 1;
        instance.Row = row;
        ordered.Insert(at, instance);
        for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        widgets.Clear(); widgets.AddRange(ordered);
        return at;
    }

    /// <summary>仅应用于该组件内容的字号倍率。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>此实例单独指定的布局与底色；普通组件无需创建外观配置。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WidgetPresentation? Presentation { get; set; }

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

    /// <summary>上课时只显示正在进行的课程。</summary>
    public bool ShowCurrentLessonOnlyOnClass { get; set; }
    /// <summary>在课表组件中隐藏已经结束的课程。</summary>
    public bool HideFinishedClass { get; set; }
    /// <summary>启用剩余时间胶囊的提前秒数，0 表示不显示胶囊。</summary>
    public int CountdownSeconds { get; set; } = 60;
    /// <summary>课表组件是否显示当前时间点的倒计时文字。</summary>
    public bool ShowTimeDetails { get; set; } = true;
    /// <summary>直接显示导入的静态文本，不展开 ClassNEX 占位符。</summary>
    public bool ImportedLiteralText { get; set; }
}

/// <summary>浮窗相对于原屏幕工作区的锚点（上/下各左中右）以及物理像素偏移。</summary>
public sealed record ImportedDockPosition(int Anchor, int OffsetX, int OffsetY, int MonitorIndex);
