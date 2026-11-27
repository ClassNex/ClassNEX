using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class NextClassWidget : WidgetBase
{
    private readonly TextBlock _text;
    private (Models.CourseSlot? Now, Models.CourseSlot? Next, bool Empty, bool Enabled, double Scale)? _last;

    public NextClassWidget()
    {
        _text = Text("", 14.5, White());

        var dot = new Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = CiPalette.SakuraAccentBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 7, 0),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { dot, _text },
        };

        View = Pill(row, padding: 10, margin: new Thickness(0, 0, WidgetGap, 0));
    }

    public override string Type => "nextclass";

    public override void Refresh(WidgetContext ctx)
    {
        string message;
        var state = (ctx.Today.Current, ctx.Today.Next, ctx.Today.IsEmpty, Config.ShowNextClass, Config.FontScale);
        if (_last == state) return;
        _last = state;
        if (Config.ShowNextClass && ctx.Today.Next is { } next)
            message = $"下节 {next.Subject} · {next.StartText}";
        else if (ctx.Today.Current is { } current)
            message = $"正在上 {current.Subject} · 至 {current.EndText}";
        else if (ctx.Today.Next is not null)
            message = "";
        else if (ctx.Today.IsEmpty)
            message = "今天没有课程";
        else
            message = "今日课程已结束";

        _text.Text = message;
        _text.FontSize = Size(14.5);
        SetContentVisible(message.Length > 0);
    }
}
