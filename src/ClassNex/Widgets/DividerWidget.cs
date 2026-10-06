using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;

/// <summary>分割线组件（对应 CI 组件库的「分割线」）：视觉上对其它组件分组。</summary>
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
        // 岛高固定 40（CI IslandContainerHeight），分割线撑满岛高
        _line.MinHeight = 40;

        // 纵向排列时分割线画成横线，横向排列时画成竖线
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
