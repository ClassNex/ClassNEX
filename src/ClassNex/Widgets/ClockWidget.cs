using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>
/// 时钟组件：显示当前时间。
/// 设置项对照 CI <c>ClockComponentSettings</c>：显示秒数 / 闪动时间分隔符。
/// （CI 的「使用实际时间」依赖时间偏移功能，本项目暂无，故不提供该设置项。）
/// </summary>
public sealed class ClockWidget : WidgetBase
{
    private readonly TextBlock _text;

    public ClockWidget()
    {
        _text = Text("", 22, White(), FontWeight.SemiBold);
        View = _text;
    }

    public override string Type => "clock";

    public override void Refresh(WidgetContext ctx)
    {
        if (Config.ShowSeconds)
        {
            _text.Text = ctx.Now.ToString("HH:mm:ss");
        }
        else if (Config.FlashTimeSeparator)
        {
            // CI ClockComponent:FlashTimeSeparator —— 冒号按秒闪动（偶数秒亮、奇数秒暗）
            var sep = ctx.Now.Second % 2 == 0 ? ":" : " ";
            _text.Text = $"{ctx.Now:HH}{sep}{ctx.Now:mm}";
        }
        else
        {
            _text.Text = ctx.Now.ToString("HH:mm");
        }

        _text.FontSize = Size(CiLarge);
    }
}
