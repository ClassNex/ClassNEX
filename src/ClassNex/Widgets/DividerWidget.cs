using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class DividerWidget : WidgetBase
{
    private readonly Border _line;

    public override string Type => "divider";

    public DividerWidget()
    {
        _line = new Border
        {
            Width = 1,
            MinHeight = 24,
            Background = White(0.28),
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        View = _line;
    }

    public override void Refresh(WidgetContext ctx)
    {
        
        _line.MinHeight = 40;

        
        if (ctx.Settings.Orientation == Models.LayoutOrientation.Vertical)
        {
            _line.Width = double.NaN;
            _line.Height = 1;
        }
        else
        {
            _line.Width = 1;
            _line.Height = double.NaN;
        }
    }
}
