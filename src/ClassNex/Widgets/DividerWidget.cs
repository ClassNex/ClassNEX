using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
            Height = 22,
            Margin = new Thickness(6, 0, 10, 0),
            Background = CiPalette.WithOpacity(CiPalette.PrimaryTextBrush(), 0.16),
            VerticalAlignment = VerticalAlignment.Center,
        };

        View = _line;
    }

    public override void Refresh(WidgetContext ctx)
    {
        if (ctx.Settings.Orientation == Models.LayoutOrientation.Vertical)
        {
            _line.Width = double.NaN;
            _line.Height = 1;
            _line.Margin = new Thickness(10, 6, 10, 6);
            _line.HorizontalAlignment = HorizontalAlignment.Stretch;
            _line.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            _line.Width = 1;
            _line.Height = 22;
            _line.Margin = new Thickness(6, 0, 10, 0);
            _line.HorizontalAlignment = HorizontalAlignment.Stretch;
            _line.VerticalAlignment = VerticalAlignment.Center;
        }
    }
}
