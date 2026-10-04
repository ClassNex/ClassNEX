namespace ClassNex.Models;

/// <summary>应用设置（持久化到 data/Settings.json）。</summary>
public sealed class AppSettings
{
    // ---------- 课表 ----------
    /// <summary>当前课表文件路径；为空则使用 data/timetable.yaml。</summary>
    public string? TimetablePath { get; set; }

    /// <summary>单周起始日期（用于判断当前是单周还是双周）。</summary>
    public DateTime SingleWeekStartTime { get; set; } = new(DateTime.Today.Year, 1, 1);

    /// <summary>多周轮换偏移。</summary>
    public int WeekRotationOffset { get; set; }

    // ---------- 主界面（悬浮课表） ----------
    public bool IsMainWindowVisible { get; set; } = true;

    public double MainWindowLeft { get; set; } = 80;

    public double MainWindowTop { get; set; } = 80;

    /// <summary>主界面是否显示日期。</summary>
    public bool ShowDate { get; set; } = true;

    /// <summary>主界面卡片背景不透明度（0~1）。</summary>
    public double BackgroundOpacity { get; set; } = 0.55;

    /// <summary>主界面是否置顶。</summary>
    public bool Topmost { get; set; } = true;

    /// <summary>主界面字号缩放。</summary>
    public double FontScale { get; set; } = 1.0;

    // ---------- 外观 ----------
    /// <summary>主题：system / light / dark。</summary>
    public string ThemeMode { get; set; } = "system";

    // ---------- 托盘 ----------
    /// <summary>点击托盘图标行为：0=显示/隐藏主界面，1=打开应用设置，2=打开档案编辑器。</summary>
    public int TrayClickBehavior { get; set; }

    // ---------- 通用 ----------
    /// <summary>上课时自动隐藏主界面（预留）。</summary>
    public bool IsHideOnClass { get; set; }
}
