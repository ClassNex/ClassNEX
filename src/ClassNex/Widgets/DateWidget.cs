using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class DateWidget : WidgetBase
{
    private readonly TextBlock _weekday;
    private readonly TextBlock _date;
    private (long Minute, string Parity, bool Clock, double Scale)? _last;

    public DateWidget()
    {
        _weekday = Text("", 9.5, White(0.58), FontWeight.Normal);
        _weekday.LineHeight = 11;

        _date = Text("", 15, White());
        _date.LineHeight = 18;

        var block = new StackPanel
        {
            Orientation = Orientation.Vertical,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = -1,
            Children = { _weekday, _date },
        };

        var accent = new Border
        {
            Width = 3,
            Height = 19,
            CornerRadius = new CornerRadius(1.5),
            Background = CiPalette.AccentBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var divider = new Border
        {
            Width = 1,
            Height = 22,
            Background = White(0.14),
            Margin = new Thickness(13, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };

        View = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = CiPalette.IslandHeight,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                accent,
                new Border { Width = 10 },
                block,
                divider,
            },
        };
    }

    public override string Type => "date";

    public override void Refresh(WidgetContext ctx)
    {
        var state = (ctx.Now.Ticks / TimeSpan.TicksPerMinute, ctx.Today.Parity, ctx.HasClock, Config.FontScale);
        if (_last == state) return;
        _last = state;
        _weekday.Text = $"{ctx.Today.DayText} · {ctx.Today.ParityText}";
        _date.Text = ctx.HasClock ? ctx.Today.DateText : $"{ctx.Today.DateText}  {ctx.Now:HH:mm}";
        _weekday.FontSize = Size(10.5);
        _weekday.LineHeight = Size(13);
        _date.FontSize = Size(15);
        _date.LineHeight = Size(18);
    }
}
