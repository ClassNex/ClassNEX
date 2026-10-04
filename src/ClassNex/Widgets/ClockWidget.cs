using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>时钟组件：显示当前时间。</summary>
public sealed class ClockWidget : WidgetBase
{
    private readonly TextBlock _text;

    public ClockWidget()
    {
        _text = Text("", 22, White(), FontWeight.SemiBold);
        View = _text;
    }

    public override string Type => "clock";

    public override void Refresh(WidgetContext ctx)
    {
        _text.Text = Config.ShowSeconds
            ? ctx.Now.ToString("HH:mm:ss")
            : ctx.Now.ToString("HH:mm");
        _text.FontSize = Size(22, ctx.Settings.FontScale);
    }
}
