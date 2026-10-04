using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>
/// 自定义文本组件：用户自由输入文本，支持占位符
/// {date} {day} {time} {parity} {next} {current} {countdown}。
/// </summary>
public sealed class TextWidget : WidgetBase
{
    private readonly TextBlock _text;

    public TextWidget()
    {
        _text = Text("", 16, White());
        View = _text;
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

        _text.FontSize = Size(CiBody, ctx.Settings.EffectiveScale);
    }
}
