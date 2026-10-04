using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>倒计时组件：距上课 / 下课还有多久。</summary>
public sealed class CountdownWidget : WidgetBase
{
    private readonly TextBlock _text;

    public CountdownWidget()
    {
        _text = Text("", 18, White());
        View = _text;
    }

    public override string Type => "countdown";

    public override void Refresh(WidgetContext ctx)
    {
        _text.Text = ctx.CountdownText;
        _text.FontSize = Size(18, ctx.Settings.FontScale);
    }
}
