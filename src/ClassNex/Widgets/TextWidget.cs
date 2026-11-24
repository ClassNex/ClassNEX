using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class TextWidget : WidgetBase
{
    private readonly TextBlock _text;
    private (string Template, long Minute, string Day, string Parity, string? Current, string? Next, string Countdown)? _lastText;
    private (bool Enabled, string? Color)? _color;

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

        var textState = (template, ctx.Now.Ticks / TimeSpan.TicksPerMinute, ctx.Today.DateText, ctx.Today.Parity, ctx.Today.Current?.DisplayName, ctx.Today.Next?.DisplayName, ctx.CountdownText);
        if (_lastText != textState)
        {
            _lastText = textState;
            _text.Text = Config.ImportedLiteralText ? template : template.Replace("{date}", ctx.Today.DateText).Replace("{day}", ctx.Today.DayText)
                .Replace("{time}", ctx.Now.ToString("HH:mm")).Replace("{parity}", ctx.Today.ParityText)
                .Replace("{countdown}", ctx.CountdownText).Replace("{current}", ctx.Today.Current?.DisplayName ?? "")
                .Replace("{next}", ctx.Today.Next?.DisplayName ?? "");
        }

        var colorState = (Config.UseCustomFontColor, Config.FontColor);
        if (_color != colorState)
        {
            _color = colorState;
            _text.Foreground = Config.UseCustomFontColor && Color.TryParse(Config.FontColor, out var custom)
                ? new SolidColorBrush(custom) : White();
        }

        _text.FontSize = Size(14.5);
    }
}
