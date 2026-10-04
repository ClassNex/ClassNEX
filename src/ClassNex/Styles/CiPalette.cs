using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Styles;

/// <summary>
/// CI（ClassIsland）配色常量 —— 全部取自 CI 本体，不在本地自创。
///
/// ★ 重要结论（已核实 CI 源码 ClassIsland/XamlThemes/FluentTheme/Styles.axaml）：
///   CI 的默认主题里**没有任何硬编码强调色**，它只使用 FluentAvalonia 的标准资源键
///   （AccentFillColorDefaultBrush / TextOnAccentFillColorPrimaryBrush /
///    SolidBackgroundFillColorSecondaryBrush / ControlElevationBorderBrush ...）。
///   因此 CI 的「默认色」= **跟随 Windows 系统强调色**，表面色 = **FluentAvalonia 深/浅色默认值**。
///   本应用因此同样不写死强调色与表面色，交由 FluentAvaloniaTheme 处理。
///
/// 下面这些常量是 CI **自己在配置文件里写死**的值，可以安全沿用：
///   data/Config/ComponentLayouts/Default.json → 主界面卡片：黑底 + 不透明度 0.5 + 圆角 8
///   data/Settings.json                        → 默认科目渐变 SecondaryColor #7FFFD4
///   ClassIsland.dll / FluentTheme/Styles.axaml → 阴影与强调覆盖色 #48000000 / #66000000 / #F4EF74
/// </summary>
public static class CiPalette
{
    // ==================== 主界面外观参数（CI ComponentLayouts 写死的值）====================

    /// <summary>CI 主界面卡片底色。</summary>
    public static readonly Color CardBackground = Colors.Black;

    /// <summary>CI 主界面卡片不透明度（ComponentLayouts: BackgroundOpacity）。</summary>
    public const double CardOpacity = 0.5;

    /// <summary>CI 主界面圆角（ComponentLayouts: CustomCornerRadius）。</summary>
    public const double CardCornerRadius = 8;

    // ==================== CI 自身写死的其它色值 ====================

    /// <summary>CI 默认科目渐变的第二色（Settings.json: SecondaryColor）。</summary>
    public static readonly Color Secondary = Color.Parse("#7FFFD4");

    /// <summary>CI 轻微叠加层 / 阴影 #48000000（Styles.axaml: BoxShadow "0 4 8 2 #48000000"）。</summary>
    public static readonly Color OverlaySubtle = Color.Parse("#48000000");

    /// <summary>CI 中等叠加层 / 阴影 #66000000（Styles.axaml: BoxShadow "0 8 32 0 #66000000"）。</summary>
    public static readonly Color OverlayMedium = Color.Parse("#66000000");

    /// <summary>CI 中性深色 #333333（ClassIsland.dll 内嵌）。</summary>
    public static readonly Color NeutralDark = Color.Parse("#333333");

    /// <summary>CI 高亮 / 提醒色 #F4EF74（ClassIsland.dll 内嵌）。</summary>
    public static readonly Color Highlight = Color.Parse("#F4EF74");

    // ==================== 强调色：与 CI 一致，取自当前主题 ====================

    /// <summary>
    /// 当前主题的强调色画刷（CI 用系统强调色，因此这里也从主题取，而不是写死一个值）。
    /// 用于进度条、填充强调元素。
    /// </summary>
    public static IBrush AccentBrush()
    {
        if (TryResource("AccentFillColorDefaultBrush", out var brush))
            return brush;

        return new SolidColorBrush(Colors.SlateBlue);
    }

    /// <summary>
    /// 列表 / 表格「选中项」的强调色画刷。
    /// 语义上对应 Fluent 的 SystemControlHighlightListAccentMediumLowBrush，
    /// 比 AccentFillColorDefaultBrush 更亮一些（CI 的课表选中格就是这种观感）。
    /// </summary>
    public static IBrush SelectionBrush()
    {
        if (TryResource("SystemControlHighlightListAccentMediumLowBrush", out var brush))
            return brush;

        if (TryResource("AccentFillColorSecondaryBrush", out var secondary))
            return secondary;

        return AccentBrush();
    }

    /// <summary>强调色上的文字画刷（Fluent 对应的 TextOnAccentFillColorPrimaryBrush）。</summary>
    public static IBrush OnAccentBrush()
    {
        if (TryResource("TextOnAccentFillColorPrimaryBrush", out var brush))
            return brush;

        return Brushes.White;
    }

    /// <summary>取当前主题的表面画刷，取不到时回退到中性色。</summary>
    public static IBrush SurfaceBrush(string key, double fallbackOpacity = 0.2)
    {
        if (TryResource(key, out var brush))
            return brush;

        return new SolidColorBrush(NeutralDark, fallbackOpacity);
    }

    // ==================== 主界面文字画笔 ====================

    /// <summary>主界面文字画刷（CI 主界面为黑底白字）。</summary>
    public static readonly IBrush OnCardBrush = Brushes.White;

    /// <summary>主界面卡片画刷（黑 @ 50%）。</summary>
    public static readonly IBrush CardBrush = new SolidColorBrush(CardBackground, CardOpacity);

    /// <summary>主界面次要文字画刷。</summary>
    public static IBrush OnCardSecondary(double opacity = 0.82) =>
        new SolidColorBrush(Colors.White, opacity);

    private static bool TryResource(string key, out IBrush brush)
    {
        brush = Brushes.Transparent;

        try
        {
            if (Application.Current?.TryFindResource(key, out var value) == true && value is IBrush found)
            {
                brush = found;
                return true;
            }
        }
        catch
        {
            // 资源系统不可用时回退
        }

        return false;
    }
}
