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

    /// <summary>列顺序：周日在前（对齐 CI ScheduleDataGrid 的列排列）。值为 CSES 的 enable_day。</summary>
    private static readonly int[] ColumnDays = { 7, 1, 2, 3, 4, 5, 6 };

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(45);

    /// <summary>
    /// 渲染周课表。选中/编辑以「格子」为单位（对齐 CI ScheduleDataGrid 的 SelectedClassInfo）：
    /// 点任意格子（有课或空的）都会回调 <paramref name="onSelectCell"/>，由调用方记录选中格，
    /// 再用右侧「编辑科目」面板排课。
    /// <paramref name="weekStart"/>：显示周的周日日期（表头显示「周日 10/04」形式）。
    /// </summary>
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

        // 2. 行定义：**只取上课节次**（课间不进网格，对齐 CI 课表编辑器）
        var rowTimes = new SortedDictionary<TimeSpan, TimeSpan>(); // start -> end

        foreach (var time in AppServices.TimeLayout.Layout.Times)
        {
            if (time.Kind == ClassTimeKind.Break)
                continue; // 课间行不放进网格
            if (TimeSpan.TryParse(time.Start, out var s) && TimeSpan.TryParse(time.End, out var e) && e > s)
                rowTimes.TryAdd(s, e);
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

        // 3. 列 / 行
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var d = 0; d < 7; d++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < rows.Count; i++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        grid.Children.Add(BuildHeader("时间", 0, 0));
        for (var c = 0; c < 7; c++)
        {
            // 表头：周日 10/04 形式（CI ScheduleDataGrid 的列头，周日在前）
            var date = week.AddDays(c);
            grid.Children.Add(BuildDayHeader(DayNames[ColumnDays[c] - 1], date, 0, c + 1));
        }

        // 4. 每行：左侧时间 + 7 个格子
        for (var i = 0; i < rows.Count; i++)
        {
            var start = rows[i];
            var end = rowTimes[start];
            var startText = start.ToString(@"hh\:mm");
            var endText = end.ToString(@"hh\:mm");

            grid.Children.Add(BuildTimeLabel(startText, endText, i + 1));

            for (var day = 1; day <= 7; day++)
            {
                var column = Array.IndexOf(ColumnDays, day) + 1;

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

    /// <summary>天列表头：两行（星期 + 日期，如「周日 / 10/04」），对齐 CI ScheduleDataGrid 的列头。</summary>
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

    /// <summary>课程单元格：**不给科目上色**（CI 做法），仅文字；选中的格子用强调色整格填充（CI 选中格样式）。</summary>
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
            // 注意：不能给 null —— Avalonia 里 Background 为 null 的控件不参与命中测试，
            // 会导致「已有课程的格子点不动」。未选中时用透明画刷（仍可点击）。
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
            // 选中格是强调色填充 → 文字用强调色上的前景色（CI 选中格）
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

    /// <summary>空格子：极淡的中性底，点击即选中该格；选中的格子用强调色整格填充（CI 的选中格）。</summary>
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
                // Fluent 的「Add」字形，替代原来的全角加号字符
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
