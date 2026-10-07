using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Styles;

namespace ClassNex.Widgets;

/// <summary>
/// 今日课表组件 —— 1:1 移植 CI（ClassIsland）主界面「课程表」组件
/// （ClassIsland.Core/Controls/LessonsControls/LessonsListBox + 三个 LessonControl）。
///
/// ★ 所有尺寸都是 CI 的**原值**（18/14/16/20 字号、40 高、16/10 间隔、8/2 胶囊内边距……），
///   整体缩放由主界面的 LayoutTransformControl(Scale=1.9) 统一处理（同 CI）。
///
/// CI 语义（LessonsListBoxItemTemplateMultiConverter + LessonControlExpanded/Minimized）：
///   - 当前项（上课或课间）= Expanded：科目全名 Bold(Emphasized 18) + 额外信息(Secondary 14，底部对齐) +
///     进度条（Canvas 贴项底、宽度=项宽、Value 0..1、50ms 过渡）
///   - 其他课 = Minimized：简称(Emphasized 18)，两侧 10×ScheduleSpacing 间隔；
///     已上完整项 Opacity 0.6（FadeCompletedClasses），且淡化有 150ms CubicEaseInOut 过渡
///   - 课间项 = 隐藏（除非是当前项）；当前课间显示「课间休息」（CI LessonControlExpanded 内置 Breaking 科目）
///   - 组件整体高 40（CI LessonsListBox Height），进度条因此正好贴在岛的底边
/// </summary>
public sealed class ScheduleWidget : WidgetBase
{
    // ---- CI 课程表组件参数（ClassIsland/Models/ComponentSettings/LessonControlSettings.cs 默认值）----

    /// <summary>CI CountdownSeconds = 60：剩余时间 ≤ 60 秒时，时间信息切换为倒计时胶囊。</summary>
    private const int CiCountdownSeconds = 60;

    private readonly StackPanel _root;

    // 当前项的可更新控件引用：每秒只更新值/文本，不重建
    private TextBlock? _timeText;
    private Control? _countdownPill;
    private Border? _pillStroke;
    private TextBlock? _pillText;
    private ProgressBar? _progress;
    private string? _currentTitle;
    private TimeSpan _currentStart;
    private TimeSpan _currentEnd;

    public override string Type => "schedule";

    /// <summary>
    /// 当前时段已进行的比例 0..1（CI LessonControlExpanded.ProgressPercent）。
    /// 无当前时段（今天没课 / 课程已全部结束）时为 null（调试自检用）。
    /// </summary>
    public double? CurrentProgress { get; private set; }

    /// <summary>布局自检信息（仅调试用：CLASSNEX_VERIFY_LAYOUT=1 时写日志核对排版）。</summary>
    public string DebugLayout()
    {
        var time = _timeText is null
            ? "time=(无)"
            : $"time={(_timeText.IsVisible ? "显示" : "隐藏")} {_timeText.Bounds.Width:0.0}x{_timeText.Bounds.Height:0.0}";
        var pill = _countdownPill is null
            ? "pill=(无)"
            : $"pill={(_countdownPill.IsVisible ? "显示" : "隐藏")} {_countdownPill.Bounds.Width:0.0}x{_countdownPill.Bounds.Height:0.0}" +
              $" r={(_pillStroke is null ? 0 : _pillStroke.CornerRadius.TopLeft):0.0}" +
              $" text={_pillText?.Bounds.Width:0.0}x{_pillText?.Bounds.Height:0.0}" +
              $" screen={_countdownPill.PointToScreen(new Avalonia.Point(0, 0))}";
        var bar = _progress is null
            ? "bar=(无)"
            : $"bar={(_progress.IsVisible ? "显示" : "隐藏")} {_progress.Bounds.Width:0.0}x{_progress.Bounds.Height:0.0}@y{_progress.Bounds.Y:0.0}";
        return $"{time} | {pill} | {bar} | progress={CurrentProgress:0.000}";
    }

    public ScheduleWidget()
    {
        _root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
            Height = 40, // CI LessonsListBox Height = 40（岛同高，进度条因此贴岛底）
        };

        View = _root;
    }

    public override void Refresh(WidgetContext ctx)
    {
        var now = ctx.Now.TimeOfDay;

        // CI ScheduleComponent：当天无课 → PlaceholderTextNoClass
        if (ctx.Today.IsEmpty)
        {
            ClearCurrent();
            CurrentProgress = null;
            if (_root.Children.Count == 0)
                _root.Children.Add(Text("今天没有课程。", Size(CiBody), White(0.92)));
            return;
        }

        var slots = ctx.Today.Slots;

        // 定位当前项（正在上的课，或落在课间空档时 = 课间休息）
        var cur = FindCurrent(slots, now);

        // 关键：当前项没变时**只更新进度值 / 剩余时间 / 倒计时胶囊，不重建**。
        if (cur is not null
            && cur.Value.Title == _currentTitle
            && cur.Value.Start == _currentStart
            && cur.Value.End == _currentEnd
            && _timeText is not null)
        {
            UpdateCurrentLive(now);
            return;
        }

        ClearCurrent();

        _currentTitle = cur?.Title;
        _currentStart = cur?.Start ?? default;
        _currentEnd = cur?.End ?? default;

        if (cur is null)
            CurrentProgress = null;

        _root.Children.Clear();

#if DEBUG
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY") == "1")
        {
            var probe = Text("早", Size(CiEmphasized), White());
            probe.Measure(Avalonia.Size.Infinity);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppContext.BaseDirectory, "_verify.log"),
                $"ScheduleWidget 科目字号 = {Size(CiEmphasized):0.00} 逻辑（CI Emphasized {CiEmphasized}，整体 ×缩放）；" +
                $"「早」实测 = 宽 {probe.DesiredSize.Width:0.0} × 高 {probe.DesiredSize.Height:0.0} 逻辑\n");
        }
#endif

        for (var i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];

            // 课间休息：上一节结束 → 本节开始之间存在空档，且当前时间落在其中时，
            // 在这个位置显示「课间休息」+ 剩余时间 + 进度条（CI：TimeType=1 仅当前项可见，Expanded 模板）
            if (i > 0)
            {
                var gapStart = slots[i - 1].End;
                var gapEnd = slot.Start;
                if (gapEnd > gapStart && now >= gapStart && now < gapEnd)
                    AddExpanded("课间休息", gapStart, gapEnd, now);
            }

            var isCurrent = slot.Contains(now);
            var isFinished = !isCurrent && slot.End <= now;

            if (isCurrent)
                AddExpanded(slot.Subject, slot.Start, slot.End, now);
            else
                _root.Children.Add(BuildMinimized(slot, isFinished));
        }
    }

    private void ClearCurrent()
    {
        _timeText = null;
        _countdownPill = null;
        _pillStroke = null;
        _pillText = null;
        _progress = null;
        _currentTitle = null;
    }

    /// <summary>
    /// 剩余时间。调试探针：CLASSNEX_VERIFY_PILL=1 时强制返回 45 秒，
    /// 让最后 60 秒的倒计时胶囊必定出现（用于核对排版，仅 Debug 构建生效）。
    /// </summary>
    private static TimeSpan LeftOf(TimeSpan end, TimeSpan now)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_PILL") == "1")
            return TimeSpan.FromSeconds(45);
#endif
        return end - now;
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

    private void AddExpanded(string title, TimeSpan start, TimeSpan end, TimeSpan now)
    {
        var (control, time, pill, stroke, pillText, progress) = BuildExpandedItem(title, start, end, now);
        _timeText = time;
        _countdownPill = pill;
        _pillStroke = stroke;
        _pillText = pillText;
        _progress = progress;
        CurrentProgress = ProgressFraction(start, end, now);
        _root.Children.Add(control);
    }

    /// <summary>
    /// 每秒更新当前项（CI LessonControlExpanded.LessonsServiceOnPostMainTimerTicked 同款逻辑）：
    /// 进度值、剩余时间文本、最后 60 秒切换倒计时胶囊。
    /// </summary>
    private void UpdateCurrentLive(TimeSpan now)
    {
        CurrentProgress = ProgressFraction(_currentStart, _currentEnd, now);
        if (_progress is not null)
            _progress.Value = CurrentProgress.Value;

        var left = LeftOf(_currentEnd, now);
        var text = "-" + FormatClock(left);
        var showPill = left >= TimeSpan.Zero && left.TotalSeconds <= CiCountdownSeconds;

        if (_timeText is not null)
        {
            _timeText.Text = text;
            _timeText.IsVisible = !showPill;
        }

        if (_countdownPill is not null && _pillText is not null)
        {
            _countdownPill.IsVisible = showPill;
            _pillText.Text = text;
        }
    }

    /// <summary>
    /// 普通课程（CI LessonControlMinimized）：简称 + 两侧 10 间隔；
    /// 已完成整项淡化 0.6（CI FadeCompletedClasses），淡化带 150ms CubicEaseInOut 过渡（CI LessonsListBox.axaml）。
    /// </summary>
    private Control BuildMinimized(CourseSlot slot, bool isFinished)
    {
        var text = Text(slot.DisplayName, Size(CiEmphasized), White());
        text.HorizontalAlignment = HorizontalAlignment.Center;

        var container = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = isFinished && Config.FadeCompletedClasses ? 0.6 : 1.0,
            Children =
            {
                new Border { Width = 10 * Config.ScheduleSpacing }, // CI：10 × ScheduleSpacing
                text,
                new Border { Width = 10 * Config.ScheduleSpacing },
            },
        };

        // CI：项容器 Opacity 的 150ms CubicEaseInOut 过渡（淡化不突兀，始终挂着）
        container.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(150),
                Easing = new CubicEaseInOut(),
            },
        };

        return container;
    }

    /// <summary>
    /// 当前项（CI LessonControlExpanded）：科目全名 Bold + 额外信息（底部对齐）+
    /// 贴项底的进度条（Canvas 横跨项宽，Value 0..1，50ms 过渡 —— 因为项高=岛高 40，
    /// 进度条正好落在浮窗岛的底边，即 CI 的「置底」形态）。
    /// 课程与课间休息共用（课间时标题为「课间休息」）。
    /// </summary>
    private (Control Control, TextBlock TimeText, Control Pill, Border Stroke, TextBlock PillText, ProgressBar Progress)
        BuildExpandedItem(string title, TimeSpan start, TimeSpan end, TimeSpan now)
    {
        var name = Text(title, Size(CiEmphasized), White(), FontWeight.Bold);
        name.VerticalAlignment = VerticalAlignment.Center;

        // 额外信息：CI ExtraInfoType=5 → 剩余时间 "-12:34"（带秒，对齐 CI SecondsToFormatTimeConverter）
        var left = LeftOf(end, now);
        var remaining = "-" + FormatClock(left);

        var time = Text(remaining, Size(CiSecondary), White(0.9));
        time.VerticalAlignment = VerticalAlignment.Bottom;
        time.Margin = new Thickness(6, 0, 0, 0);

        // CI 倒计时胶囊（最后 60 秒替换时间文本，Body 字号；Padding 8,2 / 边框 1 均为 CI 原值）
        var (pill, stroke, pillText) = BuildCountdownPill(remaining);
        pill.Margin = new Thickness(6, 0, 0, 0);

        var showPill = left >= TimeSpan.Zero && left.TotalSeconds <= CiCountdownSeconds;
        time.IsVisible = !showPill;
        pill.IsVisible = showPill;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Border { Width = 16 }, // CI：16 × ScheduleSpacing
                name,
                time,
                pill,
                new Border { Width = 16 },
            },
        };

        // CI LessonControlExpanded 的 Canvas 贴底 + ProgressBar（主题默认高度），宽度绑定项宽。
        // ★ MinWidth=0 是必须的：CI 的源码里也写了它 —— Avalonia 主题的 ProgressBar 默认 MinWidth=200，
        //   不置 0 会把当前课项撑到 200 宽（文字与后一项之间出现大空档）。
        // ★ Background（轨道）显式压暗：AvaloniaFluentUI 的默认轨道偏亮，会在青色填充后面露出一截
        //   「白边」（CI 的 FluentAvalonia 轨道是暗的，填充段之后直接是卡底）。
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = ProgressFraction(start, end, now),
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Foreground = CiPalette.AccentBrush(),
            Background = CiPalette.ProgressTrackBrush(),
        };

        // CI：进度条数值变化 50ms 过渡
        progress.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = ProgressBar.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(50),
            },
        };

        // 「当前课程」那一块的底色 —— 照 CI LessonsListBox 的做法（选中项底色 + ControlCornerRadius）。
        // ⚠️ 必须**直接包住 row**（课名 + 剩余时间/胶囊），而不是当 Grid 的兄弟节点：
        //    当兄弟节点时它的宽度会被进度条约束（110.4），比 row 窄 → 蒙版右边切在课名和时间之间，
        //    时间会露在蒙版外面（用户实测截图确认过）。包住 row 后，蒙版宽度 = 课名+时间+CI 的 16 间隔，
        //    三者天然对齐。纵向 stretch 到课项整高（40），上下内缩 1px 不越界。
        var mask = new Border
        {
            Background = CiPalette.CurrentLessonMaskBrush(),
            CornerRadius = CiPalette.LessonCornerRadius(),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = row,
        };

        var grid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            Children = { mask, progress },
        };

        return (grid, time, pill, stroke, pillText, progress);
    }

    /// <summary>
    /// CI 倒计时胶囊（LessonControlExpanded MasterTabIndex=1 分支）：
    /// 强调色 0.3 底 + 强调色描边 + 胶囊圆角（高的一半）+ Body 字号文本。
    /// Padding="8 2"、BorderThickness=1 都是 CI 原值（整体缩放由外层变换处理）。
    /// </summary>
    private static (Control Pill, Border Stroke, TextBlock Text) BuildCountdownPill(string text)
    {
        var accent = CiPalette.AccentBrush();

        IBrush background = CiPalette.TryResource("AccentFillColorTertiaryBrush", out var tertiary)
            ? tertiary
            : accent;

        var bg = new Border
        {
            Background = background,
            Opacity = 0.3,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Bottom,
            ClipToBounds = true,
        };

        var pillText = new TextBlock
        {
            Text = text,
            FontSize = CiBody, // CI MainWindowBodyFontSize = 16（整体缩放由外层变换处理）
            FontWeight = FontWeight.Medium, // CI 主界面全局字重 = Medium(500)
            Foreground = White(),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        RenderOptions.SetTextRenderingMode(pillText, TextRenderingMode.Antialias);
        // 等宽数字：胶囊里的 "-45s" 每秒变化，比例数字会让胶囊宽度抖动
        TextElement.SetFontFeatures(pillText, TabularFigures);

        var stroke = new Border
        {
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 2),
            VerticalAlignment = VerticalAlignment.Bottom,
            ClipToBounds = true,
            Child = pillText,
        };

        // CI 用 SizeDoubleToCornerRadiusConverter 把圆角绑成高度的一半 → 胶囊。
        // SizeChanged + LayoutUpdated 双保险：从隐藏(0 尺寸)变显示时，首个布局帧就拿到真实高度。
        void UpdateRadius()
        {
            var height = stroke.Bounds.Height;
            if (height <= 0)
                return;

            var radius = new CornerRadius(Math.Max(1, height / 2));
            stroke.CornerRadius = radius;
            bg.CornerRadius = radius;

            // ★ CI 原码里有这一句：背景层 Height 绑定到描边层高度
            //   （Height="{Binding Bounds.Height, ElementName=BorderStroke}"）。
            //   漏了它，背景层只有 padding 那么高（6px），会在胶囊底下露出一截小线条。
            bg.Height = height;
        }

        stroke.SizeChanged += (_, _) => UpdateRadius();
        stroke.LayoutUpdated += (_, _) => UpdateRadius();

        var pill = new Grid
        {
            // 与剩余时间文本一致：底部对齐（CI 的胶囊同样是 VerticalAlignment=Bottom），
            // 胶囊出现/消失时不会在竖直方向上跳动
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { bg, stroke },
        };

        return (pill, stroke, pillText);
    }

    /// <summary>当前时段已进行的比例 0..1（CI LessonControlExpanded ProgressPercent）。</summary>
    private static double ProgressFraction(TimeSpan start, TimeSpan end, TimeSpan now)
    {
        var total = end - start;
        if (total <= TimeSpan.Zero)
            return 0;

        var elapsed = now - start;
        return Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds, 0, 1);
    }

    /// <summary>
    /// 剩余时间格式化（对齐 CI SecondsToFormatTimeConverter 带秒样式）：
    /// 1:02:34 / 12:34 / 34s；负值按 0 处理。
    /// </summary>
    private static string FormatClock(TimeSpan left)
    {
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;

        return left.TotalSeconds switch
        {
            >= 3600 => $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}",
            >= 60 => $"{left.Minutes}:{left.Seconds:00}",
            _ => $"{left.Seconds}s",
        };
    }
}
