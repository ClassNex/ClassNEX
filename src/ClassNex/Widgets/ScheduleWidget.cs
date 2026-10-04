using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClassNex.Widgets;

/// <summary>今日课表组件：把今天的课程逐条显示在主界面上。</summary>
public sealed class ScheduleWidget : WidgetBase
{
    private readonly StackPanel _panel = new() { Spacing = 3 };

    public override string Type => "schedule";

    public ScheduleWidget() => View = _panel;

    public override void Refresh(WidgetContext ctx)
    {
        _panel.Children.Clear();

        if (ctx.Today.IsEmpty)
        {
            _panel.Children.Add(Text("今天没有课程。", Size(17, ctx.Settings.FontScale), White(0.92)));
            return;
        }

        var now = ctx.Now.TimeOfDay;

        foreach (var slot in ctx.Today.Slots)
        {
            var isCurrent = slot.Contains(now);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
            };

            row.Children.Add(Text(slot.TimeRange, Size(14, ctx.Settings.FontScale), White(0.82)));

            row.Children.Add(Text(
                slot.DisplayName,
                Size(16, ctx.Settings.FontScale),
                White(),
                isCurrent ? FontWeight.Bold : FontWeight.SemiBold));

            var detail = slot.Detail;
            if (!string.IsNullOrEmpty(detail))
                row.Children.Add(Text(detail, Size(13, ctx.Settings.FontScale), White(0.78)));

            if (isCurrent)
            {
                row.Children.Add(new TextBlock
                {
                    Text = "正在上课",
                    FontSize = Size(12, ctx.Settings.FontScale),
                    Foreground = new SolidColorBrush(Colors.White, 0.95),
                    Background = new SolidColorBrush(Colors.White, 0.18),
                    Padding = new Thickness(6, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            _panel.Children.Add(row);
        }
    }
}
