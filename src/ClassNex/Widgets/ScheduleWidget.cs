using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class ScheduleWidget : WidgetBase
{
    private const int CiCountdownSeconds = 60;

    private readonly StackPanel _root;

    private TextBlock? _timeText;
    private Control? _countdownPill;
    private TextBlock? _pillText;
    private Border? _progressTrack;
    private Border? _progressFill;
    private string? _signature;
    private TimeSpan _currentStart;
    private TimeSpan _currentEnd;

    public override string Type => "schedule";

    public double? CurrentProgress { get; private set; }

    public string DebugLayout()
    {
        var time = _timeText is null
            ? "time=(无)"
            : $"time={(_timeText.IsVisible ? "显示" : "隐藏")} {_timeText.Bounds.Width:0.0}x{_timeText.Bounds.Height:0.0}";
        var pill = _countdownPill is null
            ? "pill=(无)"
            : $"pill={(_countdownPill.IsVisible ? "显示" : "隐藏")} {_countdownPill.Bounds.Width:0.0}x{_countdownPill.Bounds.Height:0.0}" +
              $" text={_pillText?.Bounds.Width:0.0}x{_pillText?.Bounds.Height:0.0}";
        var bar = _progressTrack is null
            ? "bar=(无)"
            : $"bar={(_progressTrack.IsVisible ? "显示" : "隐藏")} {_progressTrack.Bounds.Width:0.0}x{_progressTrack.Bounds.Height:0.0}" +
              $" fill={_progressFill?.Width:0.0}";
        return $"{time} | {pill} | {bar} | progress={CurrentProgress:0.000}";
    }

    public ScheduleWidget()
    {
        _root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Height = CiPalette.IslandHeight,
        };

        View = _root;
    }

    public override void Refresh(WidgetContext ctx)
    {
        var now = ctx.Now.TimeOfDay;

        if (ctx.Profile.Schedules.Count == 0 && ctx.Profile.Subjects.Count == 0)
        {
            ClearCurrent();
            CurrentProgress = null;
            if (_signature != "import")
            {
                _root.Children.Clear();
                _root.Children.Add(BuildImportHint());
            }

            _signature = "import";
            return;
        }

        if (ctx.Today.IsEmpty)
        {
            ClearCurrent();
            CurrentProgress = null;
            if (_signature != "empty")
            {
                _root.Children.Clear();
                _root.Children.Add(BuildEmptyHint());
            }

            _signature = "empty";
            return;
        }

        var slots = ctx.Today.Slots;
        var cur = FindCurrent(slots, now);
        var slotSignature = string.Join(
            ";",
            slots.Select(slot => $"{slot.DisplayName}|{slot.Start}|{slot.End}"));
        var signature = cur is null
            ? $"{ctx.Today.DateText}|{slotSignature}|none"
            : $"{ctx.Today.DateText}|{slotSignature}|{cur.Value.Title}|{cur.Value.Start}|{cur.Value.End}";

        if (signature == _signature && _root.Children.Count > 0)
        {
            if (cur is not null)
                UpdateCurrentLive(now);
            return;
        }

        _signature = signature;
        ClearCurrent();

        _currentStart = cur?.Start ?? default;
        _currentEnd = cur?.End ?? default;
        CurrentProgress = cur is null ? null : ProgressFraction(cur.Value.Start, cur.Value.End, now);

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

            if (i > 0)
            {
                var gapStart = slots[i - 1].End;
                var gapEnd = slot.Start;
                if (gapEnd > gapStart && now >= gapStart && now < gapEnd)
                    AddExpanded("课间休息", gapStart, gapEnd, now, isBreak: true);
            }

            var isCurrent = slot.Contains(now);
            var isFinished = !isCurrent && slot.End <= now;

            if (isCurrent)
                AddExpanded(slot.Subject, slot.Start, slot.End, now);
            else
                _root.Children.Add(BuildMinimized(slot, isFinished));
        }
    }

    private Control BuildImportHint()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 6,
            Children =
            {
                Text("尚未导入课程", Size(13.5), White(0.74)),
                Text("前往导入 ›", Size(13.5), CiPalette.SakuraAccentBrush()),
            },
        };

        var pill = Pill(content, padding: 12, margin: new Thickness(0, 0, WidgetGap, 0));
        pill.Cursor = new Cursor(StandardCursorType.Hand);
        pill.PointerPressed += (_, _) => App.OpenProfileEditor(0);

        return pill;
    }

    private Control BuildEmptyHint()
    {
        var pill = Pill(
            Text("今天没有课程", Size(13.5), White(0.55)),
            padding: 12,
            margin: new Thickness(0, 0, WidgetGap, 0));

        return pill;
    }

    private void ClearCurrent()
    {
        _timeText = null;
        _countdownPill = null;
        _pillText = null;
        _progressTrack = null;
        _progressFill = null;
    }

    private static TimeSpan LeftOf(TimeSpan end, TimeSpan now)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_PILL") == "1")
            return TimeSpan.FromSeconds(45);
#endif
        return end - now;
    }

    private (string Title, TimeSpan Start, TimeSpan End)? FindCurrent(
        System.Collections.Generic.IReadOnlyList<CourseSlot> slots,
        TimeSpan now)
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

    private void AddExpanded(string title, TimeSpan start, TimeSpan end, TimeSpan now, bool isBreak = false)
    {
        var (control, time, pill, pillText, track, fill) = BuildExpandedItem(title, start, end, now, isBreak);
        _timeText = time;
        _countdownPill = pill;
        _pillText = pillText;
        _progressTrack = track;
        _progressFill = fill;
        CurrentProgress = ProgressFraction(start, end, now);
        UpdateProgressFill();
        track.SizeChanged += (_, _) => UpdateProgressFill();
        _root.Children.Add(control);
    }

    private void UpdateCurrentLive(TimeSpan now)
    {
        CurrentProgress = ProgressFraction(_currentStart, _currentEnd, now);
        UpdateProgressFill();

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

    private void UpdateProgressFill()
    {
        if (_progressTrack is null || _progressFill is null || CurrentProgress is not { } progress)
            return;

        var width = _progressTrack.Bounds.Width;
        if (width <= 0)
            return;

        _progressFill.Width = Math.Max(0, width * progress);
    }

    private Control BuildMinimized(CourseSlot slot, bool isFinished)
    {
        var text = Text(
            slot.DisplayName,
            Size(14.5),
            White(0.92),
            FontWeight.Medium);

        var spacing = Math.Clamp(Config.ScheduleSpacing, 0.5, 2.0);
        var pill = Pill(
            text,
            padding: 7,
            margin: new Thickness(0, 0, WidgetGap * spacing, 0));

        var faded = isFinished && Config.FadeCompletedClasses;
        pill.Opacity = faded ? 0.42 : 1.0;
        pill.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(160),
                Easing = new CubicEaseInOut(),
            },
        };

        return pill;
    }

    private (Control Control, TextBlock TimeText, Control Pill, TextBlock PillText, Border Track, Border Fill)
        BuildExpandedItem(string title, TimeSpan start, TimeSpan end, TimeSpan now, bool isBreak)
    {
        var name = Text(title, Size(16.5), White(), FontWeight.Bold);
        name.VerticalAlignment = VerticalAlignment.Center;

        var left = LeftOf(end, now);
        var remaining = "-" + FormatClock(left);

        var time = Text(remaining, Size(CiSecondary), White());
        time.VerticalAlignment = VerticalAlignment.Center;
        time.TextAlignment = TextAlignment.Right;
        time.MinWidth = 46;
        time.Margin = new Thickness(4, 0, 0, 0);

        var (pill, pillText) = BuildCountdownPill(remaining);
        pill.Margin = new Thickness(8, 0, 0, 0);

        var showPill = left >= TimeSpan.Zero && left.TotalSeconds <= CiCountdownSeconds;
        time.IsVisible = !showPill;
        pill.IsVisible = showPill;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            Children = { name, time, pill },
        };
        Grid.SetColumn(name, 0);
        Grid.SetColumn(time, 1);
        Grid.SetColumn(pill, 1);

        var track = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = CiPalette.SakuraAccentTint(0.16),
        };

        var fill = new Border
        {
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = CiPalette.SakuraAccentGradient(),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 0,
        };

        var progressHost = new Grid
        {
            Height = 2,
            Margin = new Thickness(12, 0, 12, 3),
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { track, fill },
        };

        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("*,5"),
            Children = { row, progressHost },
        };
        Grid.SetRow(row, 0);
        Grid.SetRow(progressHost, 1);

        var mask = new Border
        {
            Background = CiPalette.SakuraAccentTint(isBreak ? 0.12 : 0.18),
            BorderBrush = CiPalette.SakuraAccentTint(isBreak ? 0.30 : 0.46),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Height = 36,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Margin = new Thickness(0, 0, WidgetGap, 0),
            Child = layout,
        };

        return (mask, time, pill, pillText, track, fill);
    }

    private static (Control Pill, TextBlock Text) BuildCountdownPill(string text)
    {
        var pillText = Text(text, CiBody - 5, CiPalette.OnAccentBrush());
        pillText.VerticalAlignment = VerticalAlignment.Center;

        var pill = new Border
        {
            Background = CiPalette.SakuraAccentGradient(),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Child = pillText,
        };

        return (pill, pillText);
    }

    private static double ProgressFraction(TimeSpan start, TimeSpan end, TimeSpan now)
    {
        var total = end - start;
        if (total <= TimeSpan.Zero)
            return 0;

        var elapsed = now - start;
        return Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds, 0, 1);
    }

    private static string FormatClock(TimeSpan left)
    {
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;

        return left.TotalSeconds switch
        {
            >= 3600 => $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}",
            _ => $"{(int)left.TotalMinutes}:{left.Seconds:00}",
        };
    }
}
