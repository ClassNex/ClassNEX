using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Controls;










public static class TimetableGridBuilder
{
    public static readonly string[] DayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    
    private static readonly int[] ColumnDays = { 7, 1, 2, 3, 4, 5, 6 };

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(45);

    
    
    
    
    
    
    public static void Render(
        Grid grid,
        ScheduleProfile profile,
        string parity,
        Action<CellTarget>? onSelectCell = null,
        CellTarget? selectedCell = null,
        DateTime? weekStart = null)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        var week = weekStart ?? DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);

        
        var entries = new List<CourseRef>();
        foreach (var schedule in profile.Schedules)
        {
            if (!ScheduleProfile.MatchesParity(schedule.Weeks, parity))
                continue;
            if (schedule.EnableDay is < 1 or > 7)
                continue;

            foreach (var course in schedule.Classes)
                entries.Add(new CourseRef(schedule.EnableDay, schedule.Weeks, schedule, course));
        }

        
        var rowTimes = new SortedDictionary<TimeSpan, TimeSpan>();
        var breakStarts = new HashSet<TimeSpan>();

        foreach (var time in AppServices.TimeLayout.Layout.Times)
        {
            if (!TimeSpan.TryParse(time.Start, out var s) || !TimeSpan.TryParse(time.End, out var e) || e <= s)
                continue;

            rowTimes.TryAdd(s, e);
            if (time.Kind == ClassTimeKind.Break)
                breakStarts.Add(s);
        }

        foreach (var entry in entries)
        {
            if (TimeSpan.TryParse(entry.Course.StartTime, out var s)
                && TimeSpan.TryParse(entry.Course.EndTime, out var e) && e > s)
                rowTimes.TryAdd(s, e);
        }

        var rows = rowTimes.Keys.ToList();

        if (rows.Count == 0)
        {
            grid.Children.Add(BuildHint("还没有任何节次或课程。请先在「时间表」标签页添加节次，然后在「课表」标签页点击格子排课。"));
            return;
        }

        
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var d = 0; d < 7; d++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        grid.Children.Add(BuildHeader("时间", 0, 0));
        for (var c = 0; c < 7; c++)
        {
            
            var date = week.AddDays(c);
            grid.Children.Add(BuildDayHeader(DayNames[ColumnDays[c] - 1], date, 0, c + 1));
        }

        
        for (var i = 0; i < rows.Count; i++)
        {
            var start = rows[i];
            var end = rowTimes[start];
            var startText = start.ToString(@"hh\:mm");
            var endText = end.ToString(@"hh\:mm");

            var isBreakRow = breakStarts.Contains(start);
            grid.Children.Add(BuildTimeLabel(startText, endText, i + 1, isBreakRow));

            for (var day = 1; day <= 7; day++)
            {
                var column = Array.IndexOf(ColumnDays, day) + 1;

                if (isBreakRow)
                {
                    var breakCell = BuildBreakCell();
                    Grid.SetRow(breakCell, i + 1);
                    Grid.SetColumn(breakCell, column);
                    grid.Children.Add(breakCell);
                    continue;
                }

                var cellCourses = entries
                    .Where(e => e.EnableDay == day
                                && TimeSpan.TryParse(e.Course.StartTime, out var s)
                                && s == start)
                    .ToList();

                var target = new CellTarget(day, ClassTime.ToCsesTime(startText), ClassTime.ToCsesTime(endText));
                var isSelected = selectedCell is not null
                                 && selectedCell.EnableDay == day
                                 && ClassTime.SameTime(selectedCell.Start, target.Start);

                var cell = cellCourses.Count == 0
                    ? BuildEmptyCell(target, onSelectCell, isSelected)
                    : BuildCourseCell(cellCourses, target, isSelected, onSelectCell);

                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
        }
    }

    
    private static Border BuildBreakCell()
    {
        return new Border
        {
            Background = CiPalette.SurfaceBrush("SolidBackgroundFillColorTertiaryBrush", 0.5),
            Padding = new Thickness(8, 6),
            Opacity = 0.75,
            Child = new TextBlock
            {
                Text = "课间",
                FontSize = 12,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private static Border BuildHeader(string text, int row, int col)
    {
        var border = new Border
        {
            Background = CiPalette.SurfaceBrush("SolidBackgroundFillColorTertiaryBrush", 0.5),
            Padding = new Thickness(8, 8),
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.35),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        return border;
    }

    
    private static Border BuildDayHeader(string dayName, DateTime date, int row, int col)
    {
        var border = new Border
        {
            Background = CiPalette.SurfaceBrush("SolidBackgroundFillColorTertiaryBrush", 0.5),
            Padding = new Thickness(8, 6),
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.35),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Spacing = 1,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = dayName,
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 13,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = date.ToString("MM/dd"),
                        FontSize = 10.5,
                        Opacity = 0.6,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            },
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        return border;
    }

    
    private static Border BuildTimeLabel(string start, string end, int row, bool isBreak = false)
    {
        var border = new Border
        {
            Padding = new Thickness(0, 5, 10, 5),
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Opacity = isBreak ? 0.55 : 1.0,
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    new TextBlock
                    {
                        Text = start,
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    },
                    new TextBlock
                    {
                        Text = end,
                        FontSize = 11,
                        Opacity = 0.55,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    },
                },
            },
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        return border;
    }

    
    private static Border BuildCourseCell(
        List<CourseRef> courses,
        CellTarget target,
        bool isSelected,
        Action<CellTarget>? onSelect)
    {
        var panel = new StackPanel { Spacing = 2 };

        foreach (var courseRef in courses)
            panel.Children.Add(BuildCourseLine(courseRef, isSelected));

        var border = new Border
        {
            
            
            Background = isSelected ? CiPalette.AccentBrush() : Brushes.Transparent,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 2),
            Padding = new Thickness(6, 3),
            MinHeight = 34,
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel,
        };

        if (onSelect is not null)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, _) => onSelect(target);
        }

        return border;
    }

    private static Border BuildCourseLine(CourseRef courseRef, bool isSelected)
    {
        var course = courseRef.Course;
        var subject = AppServices.Schedule.Profile.FindSubject(course.Subject);

        var text = string.IsNullOrWhiteSpace(subject?.SimplifiedName)
            ? course.Subject
            : subject!.SimplifiedName!;

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(subject?.Room))
            detailParts.Add(subject!.Room!);
        if (courseRef.Weeks != "all")
            detailParts.Add(courseRef.WeeksText);

        var panel = new StackPanel { Spacing = 0 };

        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            
            Foreground = isSelected ? CiPalette.OnAccentBrush() : null,
            TextWrapping = TextWrapping.NoWrap,
        });

        if (detailParts.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", detailParts),
                FontSize = 10.5,
                Opacity = isSelected ? 0.9 : 0.6,
                Foreground = isSelected ? CiPalette.OnAccentBrush() : null,
            });
        }

        return new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 2),
            Child = panel,
        };
    }

    
    private static Border BuildEmptyCell(CellTarget target, Action<CellTarget>? onSelect, bool isSelected = false)
    {
        var border = new Border
        {
            Background = isSelected
                ? CiPalette.AccentBrush()
                : CiPalette.SurfaceBrush("SubtleFillColorSecondaryBrush", 0.12),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 2),
            MinHeight = 34,
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new AvaloniaFluentUI.Controls.FontIcon
            {
                
                Glyph = "\uE710",
                FontSize = 14,
                Opacity = isSelected ? 0.95 : 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        if (onSelect is not null)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, _) => onSelect(target);
        }

        return border;
    }

    private static TextBlock BuildHint(string message) => new()
    {
        Text = message,
        FontSize = 14,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 32, 0, 0),
        MaxWidth = 620,
    };

    public static string FormatRange(string startCses, string endCses) =>
        $"{ClassTime.ToShortTime(startCses)}–{ClassTime.ToShortTime(endCses)}";
}
