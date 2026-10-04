using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>日期组件：显示「周日 10/04」。</summary>
public sealed class DateWidget : WidgetBase
{
    private readonly TextBlock _text;

    public DateWidget()
    {
        _text = Text("", 22, White(), FontWeight.SemiBold);
        View = _text;
    }

    public override string Type => "date";

    public override void Refresh(WidgetContext ctx)
    {
        _text.Text = $"{ctx.Today.DayText} {ctx.Today.DateText}";
        _text.FontSize = Size(22, ctx.Settings.FontScale);
    }
}
