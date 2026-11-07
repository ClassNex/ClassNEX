using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Widgets;


public sealed class ClockWidget : WidgetBase
{
    private readonly TextBlock _text;

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
