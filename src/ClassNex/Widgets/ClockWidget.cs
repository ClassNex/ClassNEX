using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Widgets;






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
