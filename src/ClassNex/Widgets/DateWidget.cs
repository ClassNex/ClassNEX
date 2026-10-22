using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Widgets;







public sealed class DateWidget : WidgetBase
{
    private readonly TextBlock _text;

    public DateWidget()
    {
        _text = Text("", CiBody, White());
        View = _text;
    }

    public override string Type => "date";

    public override void Refresh(WidgetContext ctx)
    {
        _text.Text = $"{ctx.Today.DayText} {ctx.Today.DateText}";
        _text.FontSize = Size(CiBody);
        _text.FontWeight = FontWeight.Normal;
    }
}
