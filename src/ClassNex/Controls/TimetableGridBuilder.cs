using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Controls;

/// <summary>
/// 周课表网格构建器。
///
/// 用色完全对齐 CI（ClassIsland）：
///   - 单元格**不给科目上色**，只用统一的中性底 + 文字（CI 课表就是这样）
///   - 只有**选中项**使用 CI 强调青 <see cref="CiPalette.Primary"/> 填充
///   - 行之间用细分隔线区分（CI 课表的做法）
/// 交互：点课程文字选中编辑，点空格新增。
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
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        grid.Children.Add(BuildHeader("时间", 0, 0));
        for (var d = 0; d < 7; d++)
            grid.Children.Add(BuildHeader(DayNames[d], 0, d + 1));

        // 4. 每行：左侧时间 + 7 个格子
        for (var i = 0; i < rows.Count; i++)
        {
            var start = rows[i];
            var end = i + 1 < rows.Count ? rows[i + 1] : start + DefaultDuration;
            var startText = start.ToString(@"hh\:mm");
            var endText = end.ToString(@"hh\:mm");

            grid.Children.Add(BuildTimeLabel(startText, endText, i + 1));

            for (var day = 1; day <= 7; day++)
            {
                var cellCourses = entries
                    .Where(e => e.EnableDay == day
                                && TimeSpan.TryParse(e.Course.StartTime, out var s)
                                && s == start)
                    .ToList();

                var cell = cellCourses.Count == 0
                    ? BuildEmptyCell(new CellTarget(day, ClassTime.ToCsesTime(startText), ClassTime.ToCsesTime(endText)), onSelectEmpty)
                    : BuildCourseCell(cellCourses, selected, onSelectCourse);

                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, day);
                grid.Children.Add(cell);
            }
        }
    }

    /// <summary>表头：CI 用略亮的表头底色。</summary>
    private static Border BuildHeader(string text, int row, int col)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(CiPalette.SurfaceHeader, 0.55),
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

    /// <summary>左侧时间列（CI 显示起止时间）。</summary>
    private static Border BuildTimeLabel(string start, string end, int row)
    {
        var border = new Border
        {
            Padding = new Thickness(0, 5, 10, 5),
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
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

    /// <summary>课程单元格：**不加科目底色**（CI 做法），仅文字；选中的课程用 CI 强调青填充。</summary>
    private static Border BuildCourseCell(
        List<CourseRef> courses,
        CourseRef? selected,
        Action<CourseRef>? onSelect)
    {
        var panel = new StackPanel { Spacing = 2 };

        foreach (var courseRef in courses)
        {
            var isSelected = selected is not null && ReferenceEquals(selected.Course, courseRef.Course);
            panel.Children.Add(BuildCourseLine(courseRef, isSelected, onSelect));
        }

        return new Border
        {
            Padding = new Thickness(4, 2),
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = panel,
        };
    }

    private static Border BuildCourseLine(CourseRef courseRef, bool isSelected, Action<CourseRef>? onSelect)
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
            // 选中时用白字压在 CI 强调青上；未选中沿用主题前景色
            Foreground = isSelected ? Brushes.White : null,
            TextWrapping = TextWrapping.NoWrap,
        });

        if (detailParts.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", detailParts),
                FontSize = 10.5,
                Opacity = isSelected ? 0.9 : 0.6,
                Foreground = isSelected ? Brushes.White : null,
            });
        }

        var border = new Border
        {
            // 只有选中项着色 —— CI 强调青
            Background = isSelected ? CiPalette.SelectionBrush() : null,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3),
            Child = panel,
        };

        if (onSelect is not null)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, _) => onSelect(courseRef);
        }

        return border;
    }

    /// <summary>空格子：极淡的中性底，点击即可排课。</summary>
    private static Border BuildEmptyCell(CellTarget target, Action<CellTarget>? onSelect)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(CiPalette.SurfaceHeader, 0.18),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 2),
            MinHeight = 34,
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = "＋",
                FontSize = 15,
                Opacity = 0.3,
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
