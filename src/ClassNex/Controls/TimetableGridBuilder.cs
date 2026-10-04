using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Controls;

/// <summary>
/// 周课表网格构建器：渲染「7 天 x 时间段」网格。
/// 课程卡片可点击选中，空格可点击新增。
/// </summary>
public static class TimetableGridBuilder
{
    public static readonly string[] DayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(45);

    public static void Render(
        Grid grid,
        ScheduleProfile profile,
        string parity,
        CourseRef? selected = null,
        Action<CourseRef>? onSelectCourse = null,
        Action<CellTarget>? onSelectEmpty = null)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        // 1. 收集当前周次下的课程
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

        // 2. 行定义：时间表节次 + 课程出现过的开始时间
        var starts = new SortedSet<TimeSpan>();

        foreach (var time in AppServices.TimeLayout.Layout.Times)
        {
            if (TimeSpan.TryParse(time.Start, out var s))
                starts.Add(s);
        }

        foreach (var entry in entries)
        {
            if (TimeSpan.TryParse(entry.Course.StartTime, out var s))
                starts.Add(s);
        }

        var rows = starts.ToList();

        if (rows.Count == 0)
        {
            grid.Children.Add(BuildHint("还没有任何节次或课程。请先在「时间表」标签页添加节次，然后在「课表」标签页点击格子排课。"));
            return;
        }

        // 3. 列 / 行
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var d = 0; d < 7; d++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        grid.Children.Add(BuildHeader("时间", 0, 0));
        for (var d = 0; d < 7; d++)
            grid.Children.Add(BuildHeader(DayNames[d], 0, d + 1));

        // 4. 每行：左侧节次名 + 7 个格子
        for (var i = 0; i < rows.Count; i++)
        {
            var start = rows[i];
            var end = i + 1 < rows.Count ? rows[i + 1] : start + DefaultDuration;
            var startText = start.ToString(@"hh\:mm");
            var endText = end.ToString(@"hh\:mm");

            grid.Children.Add(BuildTimeLabel(startText, endText, i + 1));

            for (var day = 1; day <= 7; day++)
            {
                var cell = new StackPanel { Spacing = 2, Margin = new Thickness(2, 1) };

                var cellCourses = entries
                    .Where(e => e.EnableDay == day && TimeSpan.TryParse(e.Course.StartTime, out var s) && s == start)
                    .ToList();

                if (cellCourses.Count == 0)
                {
                    var target = new CellTarget(day, ClassTime.ToCsesTime(startText), ClassTime.ToCsesTime(endText));
                    cell.Children.Add(BuildEmptyCell(target, onSelectEmpty));
                }
                else
                {
                    foreach (var course in cellCourses)
                        cell.Children.Add(BuildCourseCard(course, selected, onSelectCourse));
                }

                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, day);
                grid.Children.Add(cell);
            }
        }
    }

    private static Border BuildHeader(string text, int row, int col)
    {
        var border = new Border
        {
            Padding = new Thickness(8, 10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = text,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        return border;
    }

    private static Border BuildTimeLabel(string start, string end, int row)
    {
        var border = new Border
        {
            Padding = new Thickness(0, 4, 8, 0),
            Child = new StackPanel
            {
                Spacing = 0,
                Children =
                {
                    new TextBlock
                    {
                        Text = start,
                        FontSize = 12,
                        FontWeight = FontWeight.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    },
                    new TextBlock
                    {
                        Text = end,
                        FontSize = 11,
                        Opacity = 0.6,
                        HorizontalAlignment = HorizontalAlignment.Right,
                    },
                },
            },
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        return border;
    }

    private static Border BuildEmptyCell(CellTarget target, Action<CellTarget>? onSelect)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(14, 128, 128, 128)),
            CornerRadius = new CornerRadius(6),
            MinHeight = 44,
            Child = new TextBlock
            {
                Text = "＋",
                FontSize = 16,
                Opacity = 0.35,
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

    private static Border BuildCourseCard(CourseRef courseRef, CourseRef? selected, Action<CourseRef>? onSelect)
    {
        var course = courseRef.Course;
        var subject = AppServices.Schedule.Profile.FindSubject(course.Subject);

        var isSelected = selected is not null &&
                         ReferenceEquals(selected.Course, course);

        var panel = new StackPanel { Spacing = 2 };

        panel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(subject?.SimplifiedName) ? course.Subject : subject!.SimplifiedName,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        });

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(subject?.Room))
            detailParts.Add(subject!.Room!);
        if (courseRef.Weeks != "all")
            detailParts.Add(courseRef.WeeksText);

        if (detailParts.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", detailParts),
                FontSize = 11,
                Foreground = Brushes.White,
                Opacity = 0.9,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var border = new Border
        {
            Background = new SolidColorBrush(SubjectColor(course.Subject)),
            BorderBrush = isSelected ? Brushes.White : null,
            BorderThickness = isSelected ? new Thickness(2) : new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5),
            Child = panel,
        };

        if (onSelect is not null)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, _) => onSelect(courseRef);
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

    private static readonly Color[] Palette =
    {
        Color.Parse("#3B82F6"), Color.Parse("#22C55E"), Color.Parse("#F59E0B"),
        Color.Parse("#EF4444"), Color.Parse("#8B5CF6"), Color.Parse("#06B6D4"),
        Color.Parse("#EC4899"), Color.Parse("#F97316"), Color.Parse("#14B8A6"),
        Color.Parse("#6366F1"),
    };

    public static Color SubjectColor(string subjectName)
    {
        var hash = 0;
        foreach (var ch in subjectName)
            hash = (hash * 31 + ch) & 0x7fffffff;

        return Palette[hash % Palette.Length];
    }

    public static string FormatRange(string startCses, string endCses) =>
        $"{ClassTime.ToShortTime(startCses)}–{ClassTime.ToShortTime(endCses)}";
}
