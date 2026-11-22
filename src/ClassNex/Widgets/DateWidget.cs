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
    private readonly Border _divider;
    private (DateTime Day, double Scale, string? Color)? _last;

    public DateWidget()
    {
        _weekday = Text("", 9.5, White(0.58), FontWeight.Normal);
        _weekday.Name = "DateDay";
        _weekday.LineHeight = 11;

        _date = Text("", 15, White());
        _date.Name = "DateValue";
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
            Background = CiPalette.SakuraAccentBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _divider = new Border
        {
            Name = "DateSeparator", IsVisible = false,
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
                _divider,
            },
        };
    }

    public override string Type => "date";

    internal bool HasFollowingItem { set => _divider.IsVisible = value; }

    public override void Refresh(WidgetContext ctx)
    {
        var state = (ctx.Now.Date, Config.FontScale, Config.UseCustomFontColor ? Config.FontColor : null);
        if (_last == state) return;
        _last = state;
        _weekday.Text = ctx.Today.DayText;
        _date.Text = ctx.Today.DateText;
        var ink = Config.UseCustomFontColor && Color.TryParse(Config.FontColor, out var custom) ? new SolidColorBrush(custom) : White();
        _date.Foreground = ink;
        _weekday.Foreground = CiPalette.WithOpacity(ink, 0.58);
        _weekday.FontSize = Size(9.5);
        _weekday.LineHeight = Size(11);
        _date.FontSize = Size(15);
        _date.LineHeight = Size(18);
    }
}
