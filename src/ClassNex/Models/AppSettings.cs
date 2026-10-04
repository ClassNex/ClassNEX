namespace ClassNex.Models;

/// <summary>主界面布局方向。</summary>
public enum LayoutOrientation
{
    Horizontal = 0,
    Vertical = 1,
}

/// <summary>应用设置（持久化到 data/Settings.json）。</summary>
public sealed class AppSettings
{
    // ---------- 课表 ----------
    /// <summary>当前课表文件路径；为空则使用 data/timetable.yaml。</summary>
    public string? TimetablePath { get; set; }

    /// <summary>单周起始日期（用于判断当前是单周还是双周）。</summary>
    public DateTime SingleWeekStartTime { get; set; } = new(DateTime.Today.Year, 1, 1);

    /// <summary>周次轮换偏移。</summary>
    public int WeekRotationOffset { get; set; }

    // ---------- 主界面（悬浮课表） ----------
    public bool IsMainWindowVisible { get; set; } = true;

    public double MainWindowLeft { get; set; } = 80;

    public double MainWindowTop { get; set; } = 80;

    /// <summary>卡片背景不透明度（0~1）。默认 0.5，取自 CI ComponentLayouts.BackgroundOpacity。</summary>
    public double BackgroundOpacity { get; set; } = 0.5;

    /// <summary>是否置顶。</summary>
    public bool Topmost { get; set; } = true;

    /// <summary>全局字号缩放。</summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>鼠标穿透：开启后点击直接落到后方窗口/桌面（CI 风格的桌面浮层行为）。</summary>
    public bool IsClickThrough { get; set; } = true;

    /// <summary>鼠标移入主界面时的淡化不透明度。</summary>
    public double HoverOpacity { get; set; } = 0.35;

    /// <summary>组件排列方向。</summary>
    public LayoutOrientation Orientation { get; set; } = LayoutOrientation.Horizontal;

    // ---------- 组件 ----------
    /// <summary>主界面组件列表（可增删、排序、单独配置）。</summary>
    public List<WidgetConfig> Widgets { get; set; } = new();

    // ---------- 时间表 ----------
    /// <summary>时间表（节次定义）。为空时会从课表反推。</summary>
    public TimeLayout TimeLayout { get; set; } = new();

    // ---------- 外观 ----------
    /// <summary>主题：system / light / dark。</summary>
    public string ThemeMode { get; set; } = "system";

    // ---------- 托盘 ----------
    /// <summary>点击托盘图标行为：0=显示/隐藏主界面，1=打开应用设置，2=打开档案编辑器。</summary>
    public int TrayClickBehavior { get; set; }

    // ---------- 通用 ----------
    /// <summary>已播种的内置示例课表版本。低于当前版本时会重新播种 data/timetable.yaml。</summary>
    public int SampleSeedVersion { get; set; }

    /// <summary>首次运行时的默认组件布局。</summary>
    public static List<WidgetConfig> CreateDefaultWidgets() => new()
    {
        new WidgetConfig { Type = "date", IsEnabled = true, Order = 0 },
        new WidgetConfig { Type = "schedule", IsEnabled = true, Order = 1 },
    };
}
