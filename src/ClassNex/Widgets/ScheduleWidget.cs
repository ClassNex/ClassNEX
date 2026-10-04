using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Styles;

namespace ClassNex.Widgets;

/// <summary>
/// 今日课表组件。形态对齐 CI（ClassIsland）主界面的「课程表」组件：
///   - 今天的科目简称以**统一白字**平铺成一行（CI 不给科目上色）
///   - 当前 / 下一节课单独加粗显示「课程名 起止时间」
///   - 当前课下方是一条 CI 强调青的进度条
/// </summary>
public sealed class ScheduleWidget : WidgetBase
{
    private readonly StackPanel _root;
    private readonly StackPanel _subjects;
    private readonly StackPanel _focusBox;
    private readonly TextBlock _focusText;
    private readonly ProgressBar _progress;

    public override string Type => "schedule";

    public ScheduleWidget()
    {
        _subjects = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _focusText = Text("", 19, White(), FontWeight.Bold);

        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 3,
            Width = 150,
            Foreground = CiPalette.AccentBrush(),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _focusBox = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _focusText, _progress },
        };

        _root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _subjects, _focusBox },
        };

        View = _root;
    }

    public override void Refresh(WidgetContext ctx)
    {
        _subjects.Children.Clear();

        if (ctx.Today.IsEmpty)
        {
            _focusBox.IsVisible = false;
            _subjects.Children.Add(Text("今天没有课程。", Size(CiBody, ctx.Settings.EffectiveScale), White(0.92)));
            return;
        }

        // 今日全部科目简称：显示**全天**课程
        // 已上完 → 淡化（对齐 CI 主界面）；正在上 → 加粗；未开始 → 常规白字
        var now = ctx.Now.TimeOfDay;

        foreach (var slot in ctx.Today.Slots)
        {
            var isCurrent = slot.Contains(now);
            var isFinished = !isCurrent && slot.End <= now;

            _subjects.Children.Add(Text(
                slot.DisplayName,
                Size(CiBody, ctx.Settings.EffectiveScale),
                isFinished ? White(0.35) : White(0.95),
                isCurrent ? FontWeight.Bold : FontWeight.SemiBold));
        }

        // 当前 / 下一节课 + 进度条
        var focus = ctx.Today.Current ?? ctx.Today.Next;
        if (focus is null)
        {
            _focusBox.IsVisible = false;
            return;
        }

        _focusBox.IsVisible = true;
        _focusText.Text = $"{focus.DisplayName} {focus.StartText}-{focus.EndText}";
        _focusText.FontSize = Size(CiBody, ctx.Settings.EffectiveScale);
        _progress.Width = Math.Max(90, 105 * ctx.Settings.EffectiveScale);
        _progress.Value = ctx.Today.Current is { } current ? ProgressOf(current, ctx.Now.TimeOfDay) : 0;
    }

    /// <summary>当前课程已进行的百分比（CI 主界面当前课下方的进度条）。</summary>
    private static double ProgressOf(CourseSlot slot, TimeSpan now)
    {
        var total = (slot.End - slot.Start).TotalSeconds;
        if (total <= 0)
            return 0;

        var done = (now - slot.Start).TotalSeconds;
        return Math.Clamp(done / total * 100, 0, 100);
    }
}
