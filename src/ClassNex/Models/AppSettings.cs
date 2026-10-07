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

    /// <summary>
    /// 主界面缩放（对应 CI Settings.json 的 Scale）。CI 默认值为 1.9，
    /// 主界面所有文字与控件都按此倍率放大。
    /// </summary>
    public double MainWindowScale { get; set; } = 1.9;

    /// <summary>最终生效的缩放 = 主界面缩放 × 全局字号缩放。</summary>
    public double EffectiveScale => MainWindowScale * FontScale;

    /// <summary>鼠标穿透：开启后点击直接落到后方窗口/桌面（CI 风格的桌面浮层行为）。</summary>
    public bool IsClickThrough { get; set; } = true;

    /// <summary>鼠标移入时主界面的目标不透明度（CI 值 0.05，见 CI MainWindowLine.axaml 的 IsLineFaded 样式）。</summary>
    public double HoverOpacity { get; set; } = 0.05;

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

    // ---------- 账户 ----------
    /// <summary>用户名（显示在设置窗口左侧账户区与账户页）。</summary>
    public string UserName { get; set; } = "LingOfficial";

    /// <summary>邮箱（显示在设置窗口左侧账户区与账户页）。</summary>
    public string Email { get; set; } = "Lingofficial0423@gamil";

    // ---------- 通知 ----------
    /// <summary>允许通知（总开关：水波纹 + 灵动通知都受它控制）。</summary>
    public bool AllowNotification { get; set; } = true;

    /// <summary>允许重要通知特效（上下课/课间休息时的全局水波纹，对照 CI 的 AllowNotificationEffect）。</summary>
    public bool AllowNotificationEffect { get; set; } = true;

    /// <summary>允许次要通知（灵动通知胶囊，对照 ClassWidgets 的 tip_toast）。</summary>
    public bool AllowMinorNotification { get; set; } = true;

    /// <summary>上课/下课/课前准备提醒设置（1:1 对照 CI 的 ClassNotificationSettings）。</summary>
    public NotificationSettings Notification { get; set; } = new();

    // ---------- 行为（对照设置页「基本 → 行为」） ----------

    /// <summary>开机自启（写入 HKCU 的 Run 项）。</summary>
    public bool RunAtStartup { get; set; }

    /// <summary>注册 classnex:// Url 协议（写入 HKCU\Software\Classes）。</summary>
    public bool RegisterUrlProtocol { get; set; }

    /// <summary>教学安全模式：崩溃时按 <see cref="CrashHandlingMode"/> 处理。</summary>
    public bool TeachingSafeMode { get; set; }

    /// <summary>崩溃处理方式：0=显示崩溃报告 1=忽略并继续 2=重新启动应用。</summary>
    public int CrashHandlingMode { get; set; }

    /// <summary>启动时显示加载界面（浮窗骨架）。</summary>
    public bool ShowStartupSplash { get; set; } = true;

    /// <summary>已播种的内置示例课表版本。低于当前版本时会重新播种 data/timetable.yaml。</summary>
    public int SampleSeedVersion { get; set; }

    /// <summary>首次运行时的默认组件布局。</summary>
    public static List<WidgetConfig> CreateDefaultWidgets() => new()
    {
        new WidgetConfig { Type = "date", IsEnabled = true, Order = 0 },
        new WidgetConfig { Type = "schedule", IsEnabled = true, Order = 1 },
    };
}

/// <summary>
/// 上课/下课/课前准备提醒设置 —— 1:1 对照 CI 的
/// <c>ClassIsland/Models/NotificationProviderSettings/ClassNotificationSettings.cs</c>（字段与默认值一致）。
/// </summary>
public sealed class NotificationSettings
{
    public bool IsClassOnNotificationEnabled { get; set; } = true;

    public bool IsClassOnPreparingNotificationEnabled { get; set; } = true;

    public bool IsClassOffNotificationEnabled { get; set; } = true;

    public int InDoorClassPreparingDeltaTime { get; set; } = 60;

    public int OutDoorClassPreparingDeltaTime { get; set; } = 600;

    public string ClassOnPreparingText { get; set; } = "准备上课，请回到座位并保持安静，做好上课准备。";

    public string ClassOnPreparingMaskText { get; set; } = "即将上课";

    public string OutdoorClassOnPreparingMaskText { get; set; } = "即将上课";

    public string OutdoorClassOnPreparingText { get; set; } = "下节课程为户外课程，请合理规划时间，做好上课准备。";

    public string ClassOnMaskText { get; set; } = "上课";

    public string ClassOffMaskText { get; set; } = "课间休息";

    public string ClassOffOverlayText { get; set; } = "";

    public bool IsSpeechEnabledOnClassPreparing { get; set; } = true;

    public bool IsSpeechEnabledOnClassOn { get; set; } = true;

    public bool IsSpeechEnabledOnClassOff { get; set; } = true;

    public bool ShowTeacherName { get; set; }
}
