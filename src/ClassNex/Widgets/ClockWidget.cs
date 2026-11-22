using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Globalization;

namespace ClassNex.Widgets;


public sealed class ClockWidget : WidgetBase
{
    private readonly TextBlock _text;
    private readonly Run _hours = new(), _separator = new(":"), _rest = new();
    private (long Tick, bool Seconds, bool Flash, double Font, string? Color)? _last;

    public ClockWidget()
    {
        _text = Text("", 20, White());
        _text.Name = "ClockValue";
        View = _text;
    }

    public override string Type => "clock";

    public override void Refresh(WidgetContext ctx)
    {
        var unit = Config.ShowSeconds || Config.FlashTimeSeparator ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        var state = (ctx.Now.Ticks / unit, Config.ShowSeconds, Config.FlashTimeSeparator, Config.FontScale,
            Config.UseCustomFontColor ? Config.FontColor : null);
        if (_last == state) return;
        if (_last is not { } previous || previous.Font != Config.FontScale || previous.Color != state.Item5)
        {
            _text.FontSize = Size(CiLarge);
            _text.Foreground = Config.UseCustomFontColor && Color.TryParse(Config.FontColor, out var color) ? new SolidColorBrush(color) : White();
        }
        _last = state;
        if (Config.ShowSeconds || !Config.FlashTimeSeparator)
        {
            if (_text.Inlines is { Count: > 0 } runs) runs.Clear();
            _text.Text = ctx.Now.ToString(Config.ShowSeconds ? "HH':'mm':'ss" : "HH':'mm", CultureInfo.InvariantCulture);
            return;
        }
        if (_text.Inlines is not { Count: > 0 })
        {
            _text.Text = null;
            _text.Inlines!.Add(_hours);
            _text.Inlines.Add(_separator);
            _text.Inlines.Add(_rest);
        }
        _hours.Text = ctx.Now.ToString("HH", CultureInfo.InvariantCulture);
        _rest.Text = ctx.Now.ToString("mm", CultureInfo.InvariantCulture);
        if (ctx.Now.Second % 2 != 0)
            _separator.Foreground = Brushes.Transparent;
        else
            _separator.ClearValue(TextElement.ForegroundProperty);
    }
}
