using Avalonia;
using Avalonia.Controls;
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

    private ProgressBar? _currentProgress;
    private string? _currentTitle;
    private TimeSpan _currentStart;
    private TimeSpan _currentEnd;

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
        var now = ctx.Now.TimeOfDay;
        var scale = ctx.Settings.EffectiveScale;

        // CI ScheduleComponent：当天无课 → PlaceholderTextNoClass
        if (ctx.Today.IsEmpty)
        {
            _currentProgress = null;
            _currentTitle = null;
            if (_root.Children.Count == 0)
                _root.Children.Add(Text("今天没有课程。", Size(CiBody, scale), White(0.92)));
            return;
        }

        var slots = ctx.Today.Slots;

        // 定位当前项（正在上的课，或落在课间空档时 = 课间休息）
        var cur = FindCurrent(slots, now);

        // 关键：当前项没变时**只更新进度条值、不重建**。
        // 否则每秒 Clear + 重建会反复销毁/重建 ProgressBar，造成肉眼可见的闪烁。
        if (cur is not null
            && cur.Value.Title == _currentTitle
            && cur.Value.Start == _currentStart
            && cur.Value.End == _currentEnd
            && _currentProgress is not null)
        {
            _currentProgress.Value = ProgressOf(cur.Value.Start, cur.Value.End, now);
            return;
        }

        _currentTitle = cur?.Title;
        _currentStart = cur?.Start ?? default;
        _currentEnd = cur?.End ?? default;
        _currentProgress = null;

        _root.Children.Clear();

#if DEBUG
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

            // 课间休息：上一节结束 → 本节开始之间存在空档，且当前时间落在其中时，
            // 在这个位置显示「课间休息 起-止」+ 进度条（CI LessonControl.xaml.cs 内置 Break 科目名）
            if (i > 0)
            {
                var gapStart = slots[i - 1].End;
                var gapEnd = slot.Start;
                if (gapEnd > gapStart && now >= gapStart && now < gapEnd)
                    AddExpanded("课间休息", gapStart, gapEnd, now, scale);
            }

            var isCurrent = slot.Contains(now);
            var isFinished = !isCurrent && slot.End <= now;

            if (isCurrent)
                AddExpanded(slot.Subject, slot.Start, slot.End, now, scale);
            else
                _root.Children.Add(BuildMinimized(slot, isFinished, scale));
        }
    }

    private (string Title, TimeSpan Start, TimeSpan End)? FindCurrent(System.Collections.Generic.IReadOnlyList<CourseSlot> slots, TimeSpan now)
    {
        for (var i = 0; i < slots.Count; i++)
        {
            if (i > 0)
            {
                var gapStart = slots[i - 1].End;
                var gapEnd = slots[i].Start;
                if (gapEnd > gapStart && now >= gapStart && now < gapEnd)
                    return ("课间休息", gapStart, gapEnd);
            }

            if (slots[i].Contains(now))
                return (slots[i].Subject, slots[i].Start, slots[i].End);
        }

        return null;
    }

    private void AddExpanded(string title, TimeSpan start, TimeSpan end, TimeSpan now, double scale)
    {
        var (control, progress) = BuildExpandedItem(title, start, end, now, scale);
        _currentProgress = progress;
        _root.Children.Add(control);
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

    /// <summary>
    /// 当前项（CI LessonControlExpanded）：全名 Bold + 「起-止」时间（底部对齐）+ 下方进度条。
    /// 课程与课间休息共用（课间时标题为「课间休息」）。
    /// </summary>
    private (Control Control, ProgressBar Progress) BuildExpandedItem(string title, TimeSpan start, TimeSpan end, TimeSpan now, double scale)
    {
        var name = Text(title, Size(CiEmphasized, scale), White(), FontWeight.Bold);
        name.VerticalAlignment = VerticalAlignment.Center;

        var time = Text($"{start:hh\\:mm}-{end:hh\\:mm}", Size(CiSecondary, scale), White(0.9));
        time.VerticalAlignment = VerticalAlignment.Bottom;
        time.Margin = new Thickness(6, 0, 0, 0);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5),
            Children =
            {
                new Border { Width = 16 * scale },
                name,
                time,
                new Border { Width = 16 * scale },
            },
        };

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = ProgressOf(start, end, now),
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = CiPalette.AccentBrush(),
        };

        return (new Grid { Children = { row, progress } }, progress);
    }

    /// <summary>当前时段已进行的百分比（CI 主界面当前项下方的进度条）。</summary>
    private static double ProgressOf(TimeSpan start, TimeSpan end, TimeSpan now)
    {
        var total = end - start;
        if (total <= TimeSpan.Zero)
            return 0;

        var elapsed = now - start;
        return Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds * 100, 0, 100);
    }
}
