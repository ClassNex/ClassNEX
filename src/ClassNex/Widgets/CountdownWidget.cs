using Avalonia.Controls;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;

/// <summary>
/// 倒计时组件。
/// 设了「目标日期」时 = CI 的 CountDownComponent（距某一天的倒计时：事件名 + 天数 + 强调色）；
/// 没设则沿用上课 / 下课倒计时。
/// </summary>
public sealed class CountdownWidget : WidgetBase
{
    private readonly TextBlock _text;

    public CountdownWidget()
    {
        _text = Text("", 18, White());
        View = _text;
    }

    public override string Type => "countdown";

    public override void Refresh(WidgetContext ctx)
    {
        if (Config.CountdownTarget is { } target)
        {
            var days = (target.Date - ctx.Now.Date).Days;
            var name = string.IsNullOrWhiteSpace(Config.CountdownName) ? "倒计时" : Config.CountdownName;
            _text.Text = days switch
            {
                > 0 => $"{name} {days} 天",
                0 => $"{name} 就是今天",
                _ => $"{name} 已过 {-days} 天",
            };

            // CI CountDownComponentSettings：强调色用于事件名与倒计时
            _text.Foreground = Config.UseCountdownAccent ? CiPalette.AccentBrush() : White();
        }
        else
        {
            _text.Text = ctx.CountdownText;
            _text.Foreground = White();
        }

        _text.FontSize = Size(CiSecondary);
    }
}
