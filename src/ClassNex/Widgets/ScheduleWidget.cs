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
















public sealed class ScheduleWidget : WidgetBase
{
    

    
    private const int CiCountdownSeconds = 60;

    private readonly StackPanel _root;

    
    private TextBlock? _timeText;
    private Control? _countdownPill;
    private Border? _pillStroke;
    private TextBlock? _pillText;
    private ProgressBar? _progress;
    private string? _currentTitle;
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
            Height = 40, 
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
            if (_root.Children.Count == 0)
                _root.Children.Add(BuildImportHint());
            return;
        }

        
        if (ctx.Today.IsEmpty)
        {
            ClearCurrent();
            CurrentProgress = null;
            if (_root.Children.Count == 0)
                _root.Children.Add(Text("今天没有课程。", Size(CiBody), White(0.92)));
            return;
        }

        var slots = ctx.Today.Slots;

        
        var cur = FindCurrent(slots, now);

        
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

    
    private Control BuildImportHint()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        panel.Children.Add(Text("还未导入课程，前往编辑档案导入课表", Size(CiBody), White(0.92)));
        panel.PointerPressed += (_, _) => App.OpenProfileEditor(0);

        return panel;
    }

    private void ClearCurrent()
    {
        _timeText = null;
        _countdownPill = null;        _pillStroke = null;
        _pillText = null;
        _progress = null;
        _currentTitle = null;
    }

    
    
    
    
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
                new Border { Width = 10 * Config.ScheduleSpacing }, 
                text,
                new Border { Width = 10 * Config.ScheduleSpacing },
            },
        };

        
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

    
    
    
    
    
    
    private (Control Control, TextBlock TimeText, Control Pill, Border Stroke, TextBlock PillText, ProgressBar Progress)
        BuildExpandedItem(string title, TimeSpan start, TimeSpan end, TimeSpan now)
    {
        var name = Text(title, Size(CiEmphasized), White(), FontWeight.Bold);
        name.VerticalAlignment = VerticalAlignment.Center;

        
        var left = LeftOf(end, now);
        var remaining = "-" + FormatClock(left);

        var time = Text(remaining, Size(CiSecondary), White(0.9));
        time.VerticalAlignment = VerticalAlignment.Bottom;
        time.Margin = new Thickness(6, 0, 0, 0);

        
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
                new Border { Width = 16 }, 
                name,
                time,
                pill,
                new Border { Width = 16 },
            },
        };

        
        
        
        
        
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

        
        progress.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = ProgressBar.ValueProperty,
                Duration = TimeSpan.FromMilliseconds(50),
            },
        };

        
        
        
        
        
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
            FontSize = CiBody, 
            FontWeight = FontWeight.Medium, 
            Foreground = White(),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };
        RenderOptions.SetTextRenderingMode(pillText, TextRenderingMode.Antialias);
        
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

        
        
        void UpdateRadius()
        {
            var height = stroke.Bounds.Height;
            if (height <= 0)
                return;

            var radius = new CornerRadius(Math.Max(1, height / 2));
            stroke.CornerRadius = radius;
            bg.CornerRadius = radius;

            
            
            
            bg.Height = height;
        }

        stroke.SizeChanged += (_, _) => UpdateRadius();
        stroke.LayoutUpdated += (_, _) => UpdateRadius();

        var pill = new Grid
        {
            
            
            VerticalAlignment = VerticalAlignment.Bottom,
            Children = { bg, stroke },
        };

        return (pill, stroke, pillText);
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
            >= 60 => $"{left.Minutes}:{left.Seconds:00}",
            _ => $"{left.Seconds}s",
        };
    }
}
