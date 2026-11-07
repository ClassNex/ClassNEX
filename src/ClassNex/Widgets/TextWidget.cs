using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class TextWidget : WidgetBase
{
    private readonly TextBlock _text;

    public TextWidget()
    {
        _text = Text("", 14.5, White());
        View = new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, WidgetGap + 2, 0),
            Child = _text,
        };
    }

    public override string Type => "text";

    public override void Refresh(WidgetContext ctx)
    {
        var template = Config.Text ?? "{date}";

        _text.Text = template
            .Replace("{date}", ctx.Today.DateText)
            .Replace("{day}", ctx.Today.DayText)
            .Replace("{time}", ctx.Now.ToString("HH:mm"))
            .Replace("{parity}", ctx.Today.ParityText)
            .Replace("{countdown}", ctx.CountdownText)
            .Replace("{current}", ctx.Today.Current?.DisplayName ?? "")
            .Replace("{next}", ctx.Today.Next?.DisplayName ?? "");

        _text.Foreground = Config.UseCustomFontColor
                           && !string.IsNullOrWhiteSpace(Config.FontColor)
                           && Color.TryParse(Config.FontColor, out var custom)
            ? new SolidColorBrush(custom)
            : White();

        _text.FontSize = Size(14.5);
    }
}
