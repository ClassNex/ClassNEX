namespace ClassNex.Models;

/// <summary>
/// 桌面组件配置。
/// 字段组织照 CI 的 <c>ClassIsland/Models/ComponentSettings/*ComponentSettings.cs</c>：
/// 通用字段（Type/IsEnabled/Order/FontScale）之外，每种组件类型有**自己的专属字段**，
/// 只有该类型在 <c>WidgetSettingsBuilder</c> 里的分支会读写它们。
/// </summary>
public sealed class WidgetConfig
{
    /// <summary>组件类型标识，见 <c>WidgetRegistry</c>。</summary>
    public string Type { get; set; } = "";

    /// <summary>是否启用（停用后不显示在主界面）。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>排序（小的在前）。</summary>
    public int Order { get; set; }

    /// <summary>字号缩放（所有组件通用）。</summary>
    public double FontScale { get; set; } = 1.0;

    // ================= 「文本」组件（对照 CI TextComponentSettings） =================

    /// <summary>文本内容，支持 {date} {time} {next} 等占位符。</summary>
    public string? Text { get; set; }

    /// <summary>是否使用自定义字体颜色（CI <c>UseCustomFontColor</c>）。</summary>
    public bool UseCustomFontColor { get; set; }

    /// <summary>自定义字体颜色（CI <c>FontColor</c>），十六进制 #RRGGBB / #AARRGGBB。</summary>
    public string? FontColor { get; set; }

    // ================= 「时钟」组件（对照 CI ClockComponentSettings） =================

    /// <summary>是否显示秒（CI <c>ShowSeconds</c>）。</summary>
    public bool ShowSeconds { get; set; }

    /// <summary>使用实际时间，即不经过时间偏移（CI <c>ShowRealTime</c>）。</summary>
    public bool ShowRealTime { get; set; } = true;

    /// <summary>闪动时间分隔符（CI <c>FlashTimeSeparator</c>，仅「不显示秒」时可用）。</summary>
    public bool FlashTimeSeparator { get; set; }

    // ================= 「课程表」组件（对照 CI LessonControlSettings 的子集） =================

    /// <summary>淡化上过的课程（CI <c>FadeCompletedClasses</c>）。</summary>
    public bool FadeCompletedClasses { get; set; } = true;

    /// <summary>课程表文本间距（CI <c>ScheduleSpacing</c>：越小越紧凑）。</summary>
    public double ScheduleSpacing { get; set; } = 1.0;

    // ================= 「当前 / 下节课」组件（CI 无此组件，本项目自定） =================

    /// <summary>是否连下一节课一起显示。</summary>
    public bool ShowNextClass { get; set; } = true;

    // ================= 「倒计时」组件（对照 CI CountDownComponentSettings 的子集） =================

    /// <summary>倒计时名称（CI <c>CountDownName</c>，作为事件名显示）。</summary>
    public string? CountdownName { get; set; }

    /// <summary>倒计时目标日期（CI 的「固定时间」模式）。</summary>
    public DateTimeOffset? CountdownTarget { get; set; }

    /// <summary>倒计时是否使用强调色。</summary>
    public bool UseCountdownAccent { get; set; } = true;
}
