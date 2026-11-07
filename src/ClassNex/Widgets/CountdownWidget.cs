using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class CountdownWidget : WidgetBase
{
    private readonly TextBlock _text;

    public CountdownWidget()
    {
        _text = Text("", 13.5, White());
        View = Pill(_text, padding: 10, margin: new Thickness(0, 0, WidgetGap, 0));
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

            _text.Foreground = Config.UseCountdownAccent ? CiPalette.SakuraAccentBrush() : White();
        }
        else
        {
            _text.Text = ctx.CountdownText;
            _text.Foreground = White();
        }

        _text.FontSize = Size(13.5);
    }
}
