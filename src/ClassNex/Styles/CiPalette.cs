using Avalonia.Media;

namespace ClassNex.Styles;

/// <summary>
/// CI（ClassIsland）配色常量 —— 全部取自 CI 本体，不在本地自创。
///
/// 来源一 · CI 安装目录配置文件：
///   data/Settings.json                          → SecondaryColor #7FFFD4
///   data/Config/ComponentLayouts/Default.json    → 主界面卡片：黑底 + 不透明度 0.5 + 圆角 8
///   app-2.1.0.1-0/ClassIsland.dll                → 内嵌色值 #333333 / #48000000 / #66000000 / #F4EF74
///
/// 来源二 · CI 运行界面截图实测（色彩直方图统计，见 _tools/analyze_ci_palette.py）：
///   强调色（选中填充）  #589499 —— 课表选中单元格 / 科目表选中行
///   强调色（深）        #426E71 —— 时间表「上课」时间点
///   强调色（亮）        #69AAAE —— 时间点块高亮边
///   课间灰              #818181 —— 时间表「课间」时间点
///   深色表面            #1A2225 / #252829 / #292D2E / #272D2E / #35393A
///
/// 重要：CI 界面里的**科目本身不上色** —— 课表单元格与主界面科目都是中性背景 + 文字，
/// 只有「选中项」和「时间表块」使用低饱和强调青。因此本应用同样不给科目分配颜色。
/// </summary>
public static class CiPalette
{
    // ==================== 强调色（CI 截图实测：低饱和青，不是亮色）====================

    /// <summary>CI 强调色（选中填充）。实测 #589499：H=184.6° S=0.27 L=0.47。</summary>
    public static readonly Color Primary = Color.Parse("#589499");

    /// <summary>CI 强调色（深）。时间表「上课」时间点。</summary>
    public static readonly Color PrimaryDeep = Color.Parse("#426E71");

    /// <summary>CI 强调色（亮）。时间点块高亮边。</summary>
    public static readonly Color PrimarySoft = Color.Parse("#69AAAE");

    /// <summary>CI 课间时间点中性灰。</summary>
    public static readonly Color BreakNeutral = Color.Parse("#818181");

    /// <summary>CI 副色（Settings.json: SecondaryColor）。</summary>
    public static readonly Color Secondary = Color.Parse("#7FFFD4");

    // ==================== CI 深色表面（截图实测，带轻微青色调）====================

    /// <summary>最深层背景（导航栏 / 侧栏）。</summary>
    public static readonly Color SurfaceDeepest = Color.Parse("#1A2225");

    /// <summary>次级表面。</summary>
    public static readonly Color SurfaceDark = Color.Parse("#252829");

    /// <summary>主内容背景 / 课表单元格。</summary>
    public static readonly Color SurfaceBase = Color.Parse("#292D2E");

    /// <summary>窗口背景。</summary>
    public static readonly Color SurfaceWindow = Color.Parse("#272D2E");

    /// <summary>表头行 / 信息条。</summary>
    public static readonly Color SurfaceHeader = Color.Parse("#35393A");

    // ==================== CI 其它内嵌色值（ClassIsland.dll）====================

    /// <summary>CI 中性深色 #333333。</summary>
    public static readonly Color NeutralDark = Color.Parse("#333333");

    /// <summary>CI 轻微叠加层 #48000000（黑 28%）。</summary>
    public static readonly Color OverlaySubtle = Color.Parse("#48000000");

    /// <summary>CI 中等叠加层 #66000000（黑 40%）。</summary>
    public static readonly Color OverlayMedium = Color.Parse("#66000000");

    /// <summary>CI 高亮 / 提醒色 #F4EF74。</summary>
    public static readonly Color Highlight = Color.Parse("#F4EF74");

    // ==================== 主界面外观参数（CI ComponentLayouts）====================

    /// <summary>CI 主界面卡片底色。</summary>
    public static readonly Color CardBackground = Colors.Black;

    /// <summary>CI 主界面卡片不透明度。</summary>
    public const double CardOpacity = 0.5;

    /// <summary>CI 主界面圆角。</summary>
    public const double CardCornerRadius = 8;

    // ==================== 画笔 ====================

    /// <summary>CI 强调色画刷。</summary>
    public static readonly IBrush PrimaryBrush = new SolidColorBrush(Primary);

    /// <summary>CI 强调色（深）画刷。</summary>
    public static readonly IBrush PrimaryDeepBrush = new SolidColorBrush(PrimaryDeep);

    /// <summary>CI 主界面卡片画刷（黑 @ 50%）。</summary>
    public static readonly IBrush CardBrush = new SolidColorBrush(CardBackground, CardOpacity);

    /// <summary>主界面文字画刷（CI 组件默认白色前景）。</summary>
    public static readonly IBrush OnCardBrush = Brushes.White;

    /// <summary>选中项画刷（CI 强调青）。</summary>
    public static IBrush SelectionBrush(double opacity = 1.0) =>
        new SolidColorBrush(Primary, opacity);

    /// <summary>主界面次要文字画刷。</summary>
    public static IBrush OnCardSecondary(double opacity = 0.82) =>
        new SolidColorBrush(Colors.White, opacity);
}
