using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;

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
    // ---- CI 的主界面字号阶梯（data\Settings.json），实际字号 = 该值 × EffectiveScale ----
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
    /// 按组件字号缩放换算字号，并叠加全局缩放。
    /// globalScale 传入 <see cref="AppSettings.EffectiveScale"/>（= CI 的 Scale × 全局字号缩放）。
    /// </summary>
    protected double Size(double baseSize, double globalScale = 1.0) =>
        baseSize * Math.Clamp(Config.FontScale, 0.5, 2.5) * Math.Clamp(globalScale, 0.4, 5.0);

    protected static IBrush White(double opacity = 1.0) =>
        new SolidColorBrush(Colors.White, opacity);

    protected static TextBlock Text(
        string text,
        double fontSize,
        IBrush? foreground = null,
        FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text,
        FontSize = fontSize,
        Foreground = foreground ?? White(),
        FontWeight = weight,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap,
    };
}
