using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>
/// 日期组件：显示「周二 10/06」。
/// 字号对齐 CI 的 DateComponent：CI 里它就是一个普通 TextBlock
/// （StringFormat="{0:ddd MM/dd}"），字号 = 继承的 MainWindowBodyFontSize（16）、字重 Normal ——
/// 所以这里统一成 Body 16 + Normal，不再用 SemiBold（否则看着比课表项还重）。
/// </summary>
public sealed class DateWidget : WidgetBase
{
    private readonly TextBlock _text;

    public DateWidget()
    {
        _text = Text("", CiBody, White());
        View = _text;
    }

    public override string Type => "date";

    public override void Refresh(WidgetContext ctx)
    {
        _text.Text = $"{ctx.Today.DayText} {ctx.Today.DateText}";
        _text.FontSize = Size(CiBody);
        _text.FontWeight = FontWeight.Normal;
    }
}
