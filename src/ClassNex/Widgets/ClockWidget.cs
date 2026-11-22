using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Widgets;


public sealed class ClockWidget : WidgetBase
{
    private readonly TextBlock _text;
    private (long Tick, bool Seconds, bool Flash, double Font)? _last;

    public ClockWidget()
    {
        _text = Text("", 20, White());
        View = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, WidgetGap + 2, 0),
            Child = _text,
        };
    }

    public override string Type => "clock";

    public override void Refresh(WidgetContext ctx)
    {
        var unit = Config.ShowSeconds || Config.FlashTimeSeparator ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        var state = (ctx.Now.Ticks / unit, Config.ShowSeconds, Config.FlashTimeSeparator, Config.FontScale);
        if (_last == state) return;
        _last = state;
        if (Config.ShowSeconds)
        {
            _text.Text = ctx.Now.ToString("HH:mm:ss");
        }
        else if (Config.FlashTimeSeparator)
        {
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
