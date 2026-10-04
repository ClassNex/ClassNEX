using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>当前 / 下节课组件。</summary>
public sealed class NextClassWidget : WidgetBase
{
    private readonly TextBlock _text;

    public NextClassWidget()
    {
        _text = Text("", 18, White(), FontWeight.SemiBold);
        View = _text;
    }

    public override string Type => "nextclass";

    public override void Refresh(WidgetContext ctx)
    {
        string message;

        if (ctx.Today.Current is { } current)
            message = $"正在上 {current.DisplayName} 至 {current.EndText}";
        else if (ctx.Today.Next is { } next)
            message = $"下节课 {next.DisplayName} {next.StartText}";
        else if (ctx.Today.IsEmpty)
            message = "今天没有课程。";
        else
            message = "今日课程已结束。";

        _text.Text = message;
        _text.FontSize = Size(18, ctx.Settings.FontScale);
    }
}
