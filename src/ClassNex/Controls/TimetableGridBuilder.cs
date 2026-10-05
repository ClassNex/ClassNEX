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
        Action<CellTarget>? onSelectEmpty = null,
        CellTarget? pending = null)
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

        // 2. 行定义：时间表节次 + 课程出现过的开始时间；课间行不可排课（对齐 CI 档案编辑器）
        var rowKinds = new SortedDictionary<TimeSpan, bool>(); // start -> 是否课间

        foreach (var time in AppServices.TimeLayout.Layout.Times)
        {
            if (TimeSpan.TryParse(time.Start, out var s))
                rowKinds.TryAdd(s, time.Kind == ClassTimeKind.Break);
        }

        foreach (var entry in entries)
        {
            if (TimeSpan.TryParse(entry.Course.StartTime, out var s))
                rowKinds[s] = false; // 这一行有课程，不是课间
        }

        var rows = rowKinds.Keys.ToList();

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
            var isBreak = rowKinds[start];
            var end = i + 1 < rows.Count ? rows[i + 1] : start + DefaultDuration;
            var startText = start.ToString(@"hh\:mm");
            var endText = end.ToString(@"hh\:mm");

            grid.Children.Add(BuildTimeLabel(startText, endText, i + 1, isBreak));

            for (var day = 1; day <= 7; day++)
            {
                // 课间行：灰色不可点击（防止把课间当空格排课）
                if (isBreak)
                {
                    var breakCell = BuildBreakCell();
                    Grid.SetRow(breakCell, i + 1);
                    Grid.SetColumn(breakCell, day);
                    grid.Children.Add(breakCell);
                    continue;
                }

                var cellCourses = entries
                    .Where(e => e.EnableDay == day
                                && TimeSpan.TryParse(e.Course.StartTime, out var s)
                                && s == start)
                    .ToList();

                var target = new CellTarget(day, ClassTime.ToCsesTime(startText), ClassTime.ToCsesTime(endText));
                var isPending = pending is not null
                                && pending.EnableDay == day
                                && ClassTime.SameTime(pending.Start, target.Start);

                var cell = cellCourses.Count == 0
                    ? BuildEmptyCell(target, onSelectEmpty, isPending)
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

    /// <summary>左侧时间列（CI 显示起止时间；课间行淡化）。</summary>
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

    /// <summary>课间行单元格：灰色、不可点击（CI 档案编辑器中的课间行同样不可排课）。</summary>
    private static Border BuildBreakCell()
    {
        return new Border
        {
            Background = CiPalette.SurfaceBrush("SubtleFillColorSecondaryBrush", 0.35),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 2),
            MinHeight = 34,
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new TextBlock
            {
                Text = "课间",
                FontSize = 11,
                Opacity = 0.35,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
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
            // 选中时用「强调色上的文字」色；未选中沿用主题前景色
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

        var border = new Border
        {
            // 只有选中项着色 —— 取当前主题的选中强调色（与 CI 一致，跟随系统强调色）
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

    /// <summary>空格子：极淡的中性底，点击即可排课；待排格子用强调色高亮（CI 图3 的选中格）。</summary>
    private static Border BuildEmptyCell(CellTarget target, Action<CellTarget>? onSelect, bool isPending = false)
    {
        var border = new Border
        {
            Background = isPending
                ? CiPalette.SelectionBrush()
                : CiPalette.SurfaceBrush("SubtleFillColorSecondaryBrush", 0.12),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 2),
            MinHeight = 34,
            BorderBrush = new SolidColorBrush(CiPalette.NeutralDark, 0.25),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new FluentAvalonia.UI.Controls.FontIcon
            {
                // Fluent 的「Add」字形，替代原来的全角加号字符
                Glyph = "\uE710",
                FontSize = 14,
                Opacity = isPending ? 0.95 : 0.35,
                Foreground = isPending ? CiPalette.OnAccentBrush() : null,
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
