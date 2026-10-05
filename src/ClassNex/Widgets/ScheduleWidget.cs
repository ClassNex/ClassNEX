using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Styles;

namespace ClassNex.Widgets;

/// <summary>
/// 今日课表组件。逻辑完全对齐 CI（ClassIsland）主界面「课程表」组件
/// （ClassIsland.Core/Controls/LessonsControls/）：
///   - 每个时间点一项：当前课 = Expanded（全名 Bold + 「起-止」时间（底部对齐）+ 下方进度条），
///     其他课 = Minimized（简称，已完成整项 Opacity 0.6），课间 = 竖线分隔符
///   - 间隔 = 10/16 逻辑像素 × ScheduleSpacing(=1)，整体再随主界面缩放（CI MainWindow ScaleTransform）
///   - 时间信息显示在「这节课自己的位置」上，没有独立悬浮框
/// </summary>
public sealed class ScheduleWidget : WidgetBase
{
    private readonly StackPanel _root;

    public override string Type => "schedule";

    public ScheduleWidget()
    {
        _root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        View = _root;
    }

    public override void Refresh(WidgetContext ctx)
    {
        _root.Children.Clear();

        // CI ScheduleComponent：当天无课 → PlaceholderTextNoClass
        if (ctx.Today.IsEmpty)
        {
            _root.Children.Add(Text("今天没有课程。", Size(CiBody, ctx.Settings.EffectiveScale), White(0.92)));
            return;
        }

        var now = ctx.Now.TimeOfDay;
        var scale = ctx.Settings.EffectiveScale;
        var slots = ctx.Today.Slots;

#if DEBUG
        // 自检探针：报告真实字号与「早」的实测宽高（供与 CI 实测值对比）
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY") == "1")
        {
            var fontSize = Size(CiEmphasized, scale);
            var probe = Text("早", fontSize, White());
            probe.Measure(Avalonia.Size.Infinity);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "_verify.log"),
                $"ScheduleWidget 科目字号 = {fontSize:0.00} 逻辑（= {CiEmphasized} × {scale:0.0}）；" +
                $"「早」实测 = 宽 {probe.DesiredSize.Width:0.0} × 高 {probe.DesiredSize.Height:0.0} 逻辑\n");
        }
#endif

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var isCurrent = slot.Contains(now);
            var isFinished = !isCurrent && slot.End <= now;

            _root.Children.Add(isCurrent
                ? BuildExpanded(slot, now, scale)
                : BuildMinimized(slot, isFinished, scale));
        }
    }

    /// <summary>普通课程（CI LessonControlMinimized）：简称 + 两侧间隔；已完成整项淡化 0.6（CI FadeCompletedClasses）。</summary>
    private Control BuildMinimized(CourseSlot slot, bool isFinished, double scale)
    {
        var text = Text(slot.DisplayName, Size(CiEmphasized, scale), White());
        text.HorizontalAlignment = HorizontalAlignment.Center;

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = isFinished ? 0.6 : 1.0,
            Children =
            {
                new Border { Width = 10 * scale },
                text,
                new Border { Width = 10 * scale },
            },
        };
    }

    /// <summary>当前课程（CI LessonControlExpanded）：全名 Bold + 「起-止」时间（底部对齐）+ 下方进度条。</summary>
    private Control BuildExpanded(CourseSlot slot, TimeSpan now, double scale)
    {
        var name = Text(slot.Subject, Size(CiEmphasized, scale), White(), FontWeight.Bold);
        name.VerticalAlignment = VerticalAlignment.Center;

        // CI：ExtraInfoType=0 → "StartTime - EndTime"（MainWindowSecondaryFontSize，底部对齐，Margin 6 0 0 0）
        var time = Text($"{slot.StartText}-{slot.EndText}", Size(CiSecondary, scale), White(0.9));
        time.VerticalAlignment = VerticalAlignment.Bottom;
        time.Margin = new Thickness(6, 0, 0, 0);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            // 底部留出进度条位置
            Margin = new Thickness(0, 0, 0, 5),
            Children =
            {
                new Border { Width = 16 * scale },
                name,
                time,
                new Border { Width = 16 * scale },
            },
        };

        // CI：进度条横跨整个课程项下方（Canvas HorizontalAlignment=Stretch, VerticalAlignment=Bottom）
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = ProgressOf(slot, now),
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = CiPalette.AccentBrush(),
        };

        return new Grid { Children = { row, progress } };
    }

    /// <summary>当前课程已进行的百分比（CI 主界面当前课下方的进度条）。</summary>
    private static double ProgressOf(CourseSlot slot, TimeSpan now)
    {
        var total = slot.End - slot.Start;
        if (total <= TimeSpan.Zero)
            return 0;

        var elapsed = now - slot.Start;
        return Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds * 100, 0, 100);
    }
}
