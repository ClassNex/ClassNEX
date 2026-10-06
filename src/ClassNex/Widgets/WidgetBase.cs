using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Widgets;

/// <summary>组件刷新上下文：一次刷新所需的全部数据。</summary>
public sealed class WidgetContext
{
    public required AppSettings Settings { get; init; }

    public required ScheduleProfile Profile { get; init; }

    public required TodaySummary Today { get; init; }

    public required DateTime Now { get; init; }

    /// <summary>倒计时文本（距上课 / 下课），由时间服务计算。</summary>
    public string CountdownText { get; init; } = "";
}

/// <summary>
/// 桌面组件基类（白皮书 ClassNEX.Widgets/WidgetBase.cs）。
/// 每个组件只构建一次控件，之后由 <see cref="Refresh"/> 更新内容。
/// </summary>
public abstract class WidgetBase
{
    // ---- CI 的主界面字号阶梯（data\Settings.json 原值）----
    // 整体缩放由主界面的 LayoutTransformControl(Scale=MainWindowScale×FontScale) 处理，
    // 组件里只用 CI 原值，不再自行乘缩放（1:1 移植 CI 主界面）。
    /// <summary>MainWindowSecondaryFontSize = 14</summary>
    protected const double CiSecondary = 14;

    /// <summary>MainWindowBodyFontSize = 16</summary>
    protected const double CiBody = 16;

    /// <summary>MainWindowEmphasizedFontSize = 18</summary>
    protected const double CiEmphasized = 18;

    /// <summary>MainWindowLargeFontSize = 20</summary>
    protected const double CiLarge = 20;

    public WidgetConfig Config { get; init; } = new();

    /// <summary>组件类型标识（对应 <see cref="WidgetRegistry"/>）。</summary>
    public abstract string Type { get; }

    /// <summary>组件根控件。</summary>
    public Control View { get; protected set; } = new Panel();

    /// <summary>根据上下文刷新显示。</summary>
    public abstract void Refresh(WidgetContext ctx);

    /// <summary>
    /// 组件字号：CI 原值 × 组件自身的字号缩放。
    /// （全局缩放 = 主界面 LayoutTransformControl 的 Scale，与 CI 一致，不在这里处理。）
    /// </summary>
    protected double Size(double baseSize) =>
        baseSize * Math.Clamp(Config.FontScale, 0.5, 2.5);

    /// <summary>
    /// 主界面文字画刷 —— **跟随主题取色**（CI 的主界面文字同样来自主题：
    /// 深色主题=浅色字、浅色主题=深色字；不能写死白色，否则浅色主题下白字看不见）。
    /// 名字保留为 White（历史调用点都从这里取），实际返回主题的文字色。
    /// </summary>
    protected static IBrush White(double opacity = 1.0)
    {
        if (CiPalette.TryResource("TextFillColorPrimaryBrush", out var brush))
        {
            if (opacity >= 1.0)
                return brush;

            if (brush is ISolidColorBrush solid)
                return new SolidColorBrush(solid.Color, Math.Clamp(solid.Opacity * opacity, 0, 1));

            return brush;
        }

        // 资源取不到时兜底：深色主题下的白字
        return new SolidColorBrush(Colors.White, opacity);
    }

    protected static TextBlock Text(
        string text,
        double fontSize,
        IBrush? foreground = null,
        FontWeight weight = FontWeight.Medium)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground ?? White(),
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };

        // 灰度抗锯齿：默认的亚像素渲染会在笔画边缘产生彩色毛边（看着像「像素点」），
        // 在半透明卡片上尤其明显。CI 的浮窗文字是干净的灰度边缘。
        RenderOptions.SetTextRenderingMode(block, TextRenderingMode.Antialias);

        // 等宽数字（OpenType tnum）：MiSans 的数字是比例宽度（"1" 比 "4" 窄），
        // 剩余时间每秒变化就会让文本宽度变来变去 → 岛宽变 → 窗口跟着缩放/位移 = 肉眼可见的「抖」。
        TextElement.SetFontFeatures(block, TabularFigures);
        return block;
    }

    /// <summary>等宽数字特性集（tnum），供需要「宽度不随时间变化」的文本使用。</summary>
    protected static readonly FontFeatureCollection TabularFigures = new()
    {
        FontFeature.Parse("tnum"),
    };
}
