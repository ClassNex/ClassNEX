using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

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
    /// 「当前课程」那一块的底色 —— 照搬 CI 自己的写法：
    /// `ClassIsland.Core/Controls/LessonsControls/LessonsListBox.axaml`
    ///   L104 `Background = ListViewItemBackground`（普通项）
    ///   L156 `Background = ListViewItemBackgroundSelected`（**当前项 = 选中项**）
    ///   L105 `CornerRadius = ControlCornerRadius`
    /// 所以 CI 的当前课项是「一层柔和的选中底色」，不是一块生硬的实心暗色。
    /// 圆角也用 CI 的 ControlCornerRadius。
    /// </summary>
    public static IBrush CurrentLessonMaskBrush()
    {
        // 优先用主题里的选中底色（= CI 用的那把键）
        if (TryResource("ListViewItemBackgroundSelected", out var selected))
            return selected;

        // 兜底：Fluent 深色主题下「选中」是一层很淡的白（约 6%），浅色主题是一层很淡的黑
        var dark = IsDarkTheme();
        return dark
            ? new SolidColorBrush(Colors.White, 0.08)
            : new SolidColorBrush(Colors.Black, 0.05);
    }

    /// <summary>CI 的 ControlCornerRadius（当前课项底色的圆角）。</summary>
    public static CornerRadius LessonCornerRadius()
    {
        // 注意：TryResource 是给画刷用的（返回 IBrush），圆角要走资源宿主自己查
        if (Application.Current is { } app &&
            app.TryFindResource("ControlCornerRadius", out var value) &&
            value is CornerRadius radius)
        {
            return radius;
        }

        // Fluent 的 ControlCornerRadius 标准值
        return new CornerRadius(4);
    }

    /// <summary>
    /// 启动骨架占位块的底色 —— 比卡片亮一档的柔和块（深浅主题各自取色），
    /// 对应「组件还没加载出来时」的占位样式。
    /// </summary>
    public static IBrush SkeletonBrush()
    {
        return IsDarkTheme()
            ? new SolidColorBrush(Colors.White, 0.16)
            : new SolidColorBrush(Colors.Black, 0.07);
    }

    /// <summary>
    /// 进度条的「轨道」画刷（未填充部分）—— CI 的 ProgressBar（FluentAvalonia）默认轨道。
    /// FluentAvalonia 深色主题里轨道用的是 `ControlFillColorDefault` = **#0FFFFFFF**（白 6%）、
    /// 浅色主题是 **#B3FFFFFF**（叠加在浅色卡上≈看不见）。CI 的观感是「填充段 + 一段淡灰轨道」，
    /// 这里按同色系给到看得见的程度。
    /// </summary>
    public static IBrush ProgressTrackBrush()
    {
        var dark = IsDarkTheme();
        return dark
            ? new SolidColorBrush(Colors.White, 0.18)
            : new SolidColorBrush(Colors.Black, 0.18);
    }

    /// <summary>
    /// 当前是不是深色主题。
    /// ⚠️ **不要用 `Application.Current.ActualThemeVariant`** —— 本项目用 FluentAvaloniaTheme 的
    /// `PreferSystemTheme` 切主题，Application 的 ActualThemeVariant 并不跟着它走（实测：App 明明是
    /// 深色，它却报 Light），据此选色会把深色卡刷成浅色卡、描边刷成黑色（=看不见）。
    /// 可靠做法：看主题实际解析出来的主文字色 —— 深色主题下它是白的。
    /// </summary>
    public static bool IsDarkTheme()
    {
        if (TryResource("TextFillColorPrimaryBrush", out var brush) && brush is ISolidColorBrush solid)
        {
            var c = solid.Color;
            var luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luma > 0.5;
        }

        return true;
    }

    /// <summary>
    /// 岛的 1px 描边 —— 照搬 CI `.line-background` 的
    /// `BorderBrush="{DynamicResource ControlElevationBorderBrush}"`。
    ///
    /// 值取自 FluentAvalonia 源码（CI 用的就是这包）`Styling/StylesV2/Fluentv2Colors.axaml`：
    ///   深色：ControlStrokeColorSecondary = #18FFFFFF（上）/ ControlStrokeColorDefault = #12FFFFFF（下）
    ///   浅色：ControlStrokeColorSecondary = #29000000（上）/ ControlStrokeColorDefault = #0F000000（下）
    /// WinUI 的 ControlElevationBorderBrush 就是「上亮下暗」的竖向渐变，这里按同样形状实现。
    ///
    /// ⚠️ 补偿：CI 的描边画在带 `Opacity = BackgroundOpacity(0.5)` 的 Border 里，等于又被压掉一半 ——
    /// 照抄原值会几乎看不见，所以 alpha 乘 2（保持同形状、同色相）。
    /// </summary>
    public static IBrush IslandBorderBrush()
    {
        var dark = IsDarkTheme();

        // 上端 = ControlStrokeColorSecondary，下端 = ControlStrokeColorDefault（×2 补偿岛自身的 0.5 透明度）
        var top = dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x52, 0x00, 0x00, 0x00);
        var bottom = dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1E, 0x00, 0x00, 0x00);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(top, 0.33),
                new GradientStop(bottom, 1.0),
            },
        };
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

    public static bool TryResource(string key, out IBrush brush)
    {
        brush = Brushes.Transparent;

        try
        {
            var app = Application.Current;
            if (app is null)
                return false;

            // 关键：必须带上当前主题变体查询。
            // TryFindResource(key) 不带变体时返回的是**浅色**变体的值，
            // 会出现在深色界面里画出白色卡片（用户反馈的「发白」）的问题。
            if (app.TryFindResource(key, app.ActualThemeVariant, out var value) && value is IBrush themed)
            {
                brush = themed;
                return true;
            }

            if (app.TryFindResource(key, out var fallback) && fallback is IBrush plain)
            {
                brush = plain;
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
