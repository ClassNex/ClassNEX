using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Controls;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

/// <summary>
/// 档案编辑器（三页分别对齐 CI 截图 3 / 4 / 5）：
///   课表   —— 点科目 → 点格子直接排课（含「选完科目自动移动到下一个课程」）
///   时间表 —— 可视化时间轴（上课=强调色块，课间=中性块）
///   科目   —— 表格（科目名 / 简称 / 户外课程 / 科任老师）
/// </summary>
public partial class ProfileEditorWindow : Window
{
    private bool _loading;
    private CourseRef? _selectedCourse;
    private ClassTime? _selectedTime;
    private Subject? _currentSubject;
    private Subject? _paletteSubject;
    private CellTarget? _pendingCell;

    public ProfileEditorWindow()
    {
        InitializeComponent();
        AutoAdvanceCheck.IsChecked = true;
        WireEvents();

        ParityCombo.SelectedIndex = 0;
        LoadAll();
    }

    /// <summary>切换到指定标签页（0=课表 / 1=时间表 / 2=科目 / 3=调课）。</summary>
    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.ItemCount)
            Tabs.SelectedIndex = index;
    }

    private void WireEvents()
    {
        ParityCombo.SelectionChanged += (_, _) => RenderTimetable();
        AutoAdvanceCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == CheckBox.IsCheckedProperty)
                RefreshPalette();
        };

        RefreshButton.Click += (_, _) => { AppServices.ReloadTimetable(); LoadAll(); };
        NewCourseButton.Click += (_, _) =>
        {
            _pendingCell = null;
            _selectedCourse = null;
            ResetCourseEditor();
            RenderTimetable();
            RefreshPalette();
        };
        DeleteCourseButton.Click += (_, _) => DeleteCourse();
        AddCourseButton.Click += (_, _) => AddCourse();
        UpdateCourseButton.Click += (_, _) => UpdateCourse();

        AddTimeButton.Click += (_, _) => AddTime();
        RemoveTimeButton.Click += (_, _) => RemoveTime();
        MoveUpButton.Click += (_, _) => MoveTime(-1);
        MoveDownButton.Click += (_, _) => MoveTime(1);
        ResetTimeButton.Click += (_, _) => ResetTimeLayout();
        SaveTimeButton.Click += (_, _) => SaveTimeEdit();

        AddSubjectButton.Click += (_, _) => AddSubject();
        DeleteSubjectButton.Click += (_, _) => DeleteSubject();
        SaveSubjectsButton.Click += (_, _) =>
        {
            AppServices.Schedule.Save();
            CourseHintText.Text = "科目已保存到课表文件。";
        };

        SubjectNameBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectSimplifiedBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectTeacherBox.TextChanged += (_, _) => PushSubjectEdit();
        OutdoorCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == CheckBox.IsCheckedProperty)
                PushSubjectEdit();
        };

        OpenButton.Click += OnOpenTimetable;
        ExportButton.Click += OnExport;
        ReloadButton.Click += (_, _) => { AppServices.ReloadTimetable(); LoadAll(); };
    }

    // ==================== 加载 ====================

    private void LoadAll()
    {
        _loading = true;
        _selectedCourse = null;
        _selectedTime = null;
        _pendingCell = null;
        _paletteSubject = null;

        RefreshSubjectCombo();
        RefreshPalette();
        RefreshSubjectTable();
        RefreshTimeline();
        RefreshInfo();

        _loading = false;

        ResetCourseEditor();
        RenderTimetable();
    }

    private void RefreshInfo() => ProfileInfoText.Text = $"当前课表文件：{AppServices.TimetablePath}";

    private void RefreshSubjectCombo()
    {
        var names = AppServices.Schedule.Profile.Subjects.Select(s => s.Name).ToList();
        var previous = CourseSubjectCombo.SelectedItem as string;

        CourseSubjectCombo.ItemsSource = names;

        if (previous is not null && names.Contains(previous))
            CourseSubjectCombo.SelectedItem = previous;
        else if (names.Count > 0)
            CourseSubjectCombo.SelectedIndex = 0;
    }

    // ==================== 课表（图3）====================

    private void RenderTimetable()
    {
        var parity = ParityCombo.SelectedIndex switch
        {
            1 => "odd",
            2 => "even",
            _ => "all",
        };

        TimetableGridBuilder.Render(
            TimetableGrid,
            AppServices.Schedule.Profile,
            parity,
            _selectedCourse,
            SelectCourse,
            OnEmptyCell,
            _pendingCell);
    }

    /// <summary>科目面板（CI 图3 右侧）：点科目 → 已选中格子则直接排课。</summary>
    private void RefreshPalette()
    {
        SubjectPalettePanel.Children.Clear();

        foreach (var subject in AppServices.Schedule.Profile.Subjects)
        {
            var picked = ReferenceEquals(subject, _paletteSubject);

            var button = new Button
            {
                Content = subject.DisplayName,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 4),
                FontWeight = picked ? FontWeight.Bold : FontWeight.Normal,
            };

            if (picked)
            {
                button.Background = CiPalette.SelectionBrush();
                button.Foreground = CiPalette.OnAccentBrush();
            }

            button.Click += (_, _) => PickSubject(subject);
            SubjectPalettePanel.Children.Add(button);
        }

        PaletteHintText.Text = _pendingCell is { } cell
            ? $"已选 {TimetableGridBuilder.DayNames[cell.EnableDay - 1]} " +
              $"{ClassTime.ToShortTime(cell.Start)}–{ClassTime.ToShortTime(cell.End)}，点科目即可排课。"
            : _paletteSubject is { } picked2
                ? $"当前科目：{picked2.DisplayName}，点课表空格快速排课。"
                : "点课表空格 → 再点科目即可排课；也可以先选科目再点空格。";
    }

    private void PickSubject(Subject subject)
    {
        // 流程一（CI 图3）：已选中空格 → 点科目直接排课
        if (_pendingCell is { } cell)
        {
            Assign(cell, subject);

            _pendingCell = AutoAdvanceCheck.IsChecked == true ? NextCell(cell) : null;
            _paletteSubject = subject;

            RefreshPalette();
            RenderTimetable();
            return;
        }

        // 流程二：先选科目，再点空格
        _paletteSubject = ReferenceEquals(_paletteSubject, subject) ? null : subject;
        CourseSubjectCombo.SelectedItem = _paletteSubject?.Name;
        RefreshPalette();
    }

    private void OnEmptyCell(CellTarget target)
    {
        if (_paletteSubject is { } subject)
        {
            Assign(target, subject);
            RenderTimetable();
            return;
        }

        _pendingCell = target;
        _selectedCourse = null;
        PrefillFromEmptyCell(target);
        RefreshPalette();
        RenderTimetable();
    }

    private void Assign(CellTarget cell, Subject subject)
    {
        AppServices.Schedule.AddCourse(cell.EnableDay, "all", new Course
        {
            Subject = subject.Name,
            StartTime = cell.Start,
            EndTime = cell.End,
        });

        CourseHintText.Text =
            $"已在 {TimetableGridBuilder.DayNames[cell.EnableDay - 1]} " +
            $"{ClassTime.ToShortTime(cell.Start)}–{ClassTime.ToShortTime(cell.End)} 排入「{subject.DisplayName}」。";
    }

    /// <summary>自动移动到下一个课程：同一行往后，到底则换到下周的第一行。</summary>
    private CellTarget? NextCell(CellTarget current)
    {
        var times = AppServices.TimeLayout.Layout.Times
            .Where(t => t.Kind == ClassTimeKind.Class)
            .ToList();

        if (times.Count == 0)
            return null;

        var index = times.FindIndex(t => ClassTime.SameTime(t.Start, ClassTime.ToShortTime(current.Start)));
        if (index < 0)
            index = 0;

        if (index + 1 < times.Count)
        {
            var next = times[index + 1];
            return new CellTarget(current.EnableDay, ClassTime.ToCsesTime(next.Start), ClassTime.ToCsesTime(next.End));
        }

        var nextDay = current.EnableDay % 7 + 1;
        return new CellTarget(nextDay, ClassTime.ToCsesTime(times[0].Start), ClassTime.ToCsesTime(times[0].End));
    }

    private void ResetCourseEditor()
    {
        CourseEditorTitle.Text = "课程";
        CourseHintText.Text = "点课表空格选中，再点上方科目排课。";
    }

    private void SelectCourse(CourseRef courseRef)
    {
        _selectedCourse = courseRef;
        _pendingCell = null;
        _loading = true;

        CourseEditorTitle.Text = "编辑课程";
        CourseDayCombo.SelectedIndex = Math.Clamp(courseRef.EnableDay - 1, 0, 6);
        CourseSubjectCombo.SelectedItem = courseRef.Course.Subject;
        CourseStartPicker.SelectedTime = ParseTime(courseRef.Course.StartTime);
        CourseEndPicker.SelectedTime = ParseTime(courseRef.Course.EndTime);
        CourseWeeksCombo.SelectedIndex = WeeksToIndex(courseRef.Weeks);
        CourseHintText.Text =
            $"已选中：{TimetableGridBuilder.DayNames[courseRef.EnableDay - 1]} " +
            $"{courseRef.Course.Subject} {TimetableGridBuilder.FormatRange(courseRef.Course.StartTime, courseRef.Course.EndTime)}（{courseRef.WeeksText}）";

        _loading = false;
        RenderTimetable();
        RefreshPalette();
    }

    private void PrefillFromEmptyCell(CellTarget target)
    {
        _loading = true;
        CourseEditorTitle.Text = "新增课程";
        CourseDayCombo.SelectedIndex = Math.Clamp(target.EnableDay - 1, 0, 6);
        CourseStartPicker.SelectedTime = ParseTime(target.Start);
        CourseEndPicker.SelectedTime = ParseTime(target.End);
        CourseWeeksCombo.SelectedIndex = 0;
        _loading = false;
    }

    private void AddCourse()
    {
        if (CourseSubjectCombo.SelectedItem is not string subject || string.IsNullOrWhiteSpace(subject))
        {
            CourseHintText.Text = "请先在「科目」标签页添加科目。";
            return;
        }

        if (!TryReadTimes(out var start, out var end))
            return;

        AppServices.Schedule.AddCourse(CourseDayCombo.SelectedIndex + 1, IndexToWeeks(CourseWeeksCombo.SelectedIndex),
            new Course
            {
                Subject = subject,
                StartTime = ToCses(start),
                EndTime = ToCses(end),
            });

        _selectedCourse = null;
        CourseHintText.Text = "已新增课程。";
        RenderTimetable();
    }

    private void UpdateCourse()
    {
        if (_selectedCourse is null)
        {
            CourseHintText.Text = "请先点击课表中的一个课程。";
            return;
        }

        if (CourseSubjectCombo.SelectedItem is not string subject || string.IsNullOrWhiteSpace(subject))
            return;

        if (!TryReadTimes(out var start, out var end))
            return;

        var day = CourseDayCombo.SelectedIndex + 1;
        var weeks = IndexToWeeks(CourseWeeksCombo.SelectedIndex);
        var updated = new Course { Subject = subject, StartTime = ToCses(start), EndTime = ToCses(end) };

        var original = _selectedCourse;
        if (original.EnableDay == day && original.Weeks == weeks)
            AppServices.Schedule.UpdateCourse(day, weeks, original.Course, updated);
        else
        {
            AppServices.Schedule.RemoveCourse(original.EnableDay, original.Weeks, original.Course);
            AppServices.Schedule.AddCourse(day, weeks, updated);
        }

        _selectedCourse = null;
        CourseHintText.Text = "已保存修改。";
        RenderTimetable();
    }

    private void DeleteCourse()
    {
        if (_selectedCourse is null)
        {
            CourseHintText.Text = "请先点击课表中的一个课程。";
            return;
        }

        AppServices.Schedule.RemoveCourse(_selectedCourse.EnableDay, _selectedCourse.Weeks, _selectedCourse.Course);
        _selectedCourse = null;
        CourseHintText.Text = "已删除课程。";
        RenderTimetable();
    }

    private bool TryReadTimes(out TimeSpan start, out TimeSpan end)
    {
        start = CourseStartPicker.SelectedTime ?? TimeSpan.FromHours(8);
        end = CourseEndPicker.SelectedTime ?? start.Add(TimeSpan.FromMinutes(45));

        if (end <= start)
        {
            CourseHintText.Text = "结束时间必须晚于开始时间。";
            return false;
        }

        return true;
    }

    // ==================== 时间表（图4）====================

    private void RefreshTimeline()
    {
        TimeAxisPanel.Children.Clear();

        var times = AppServices.TimeLayout.Layout.Times;
        if (times.Count == 0)
        {
            TimeAxisPanel.Children.Add(new TextBlock
            {
                Text = "时间表为空。点「从课表重建」按当前课表生成，或点「添加」。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            return;
        }

        foreach (var time in times)
        {
            var isBreak = time.Kind == ClassTimeKind.Break;
            var isSelected = ReferenceEquals(time, _selectedTime);

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("62,*") };

            var label = new TextBlock
            {
                Text = time.Start,
                FontSize = 12,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var foreground = isBreak ? null : CiPalette.OnAccentBrush();

            var block = new Border
            {
                Background = isBreak
                    ? CiPalette.SurfaceBrush("ControlAltFillColorSecondaryBrush", 0.4)
                    : isSelected ? CiPalette.SelectionBrush() : CiPalette.AccentBrush(),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5),
                MinHeight = isBreak ? 26 : 40,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Spacing = 0,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"{time.Start} - {time.End}",
                            FontSize = 13,
                            Foreground = foreground,
                        },
                        new TextBlock
                        {
                            Text = $"{time.Name}　{DurationText(time)}",
                            FontSize = 11,
                            Opacity = 0.85,
                            Foreground = foreground,
                        },
                    },
                },
            };

            block.PointerPressed += (_, _) => SelectTime(time);
            Grid.SetColumn(block, 1);
            row.Children.Add(block);

            TimeAxisPanel.Children.Add(row);
        }
    }

    private static string DurationText(ClassTime time)
    {
        if (!TimeSpan.TryParse(time.Start, out var start) || !TimeSpan.TryParse(time.End, out var end))
            return "";

        var d = end - start;
        return $"{d.Hours:00}:{d.Minutes:00}:{d.Seconds:00}";
    }

    private void SelectTime(ClassTime time)
    {
        _selectedTime = time;
        _loading = true;

        TimeEditorTitle.Text = $"编辑时间点 · {time.Name}";
        TimeNameBox.Text = time.Name;
        TimeStartPicker.SelectedTime = ParseTime(time.Start);
        TimeEndPicker.SelectedTime = ParseTime(time.End);
        TimeKindCombo.SelectedIndex = time.Kind == ClassTimeKind.Break ? 1 : 0;

        _loading = false;
        RefreshTimeline();
    }

    private void AddTime()
    {
        var time = new ClassTime
        {
            Name = $"第{AppServices.TimeLayout.Layout.Times.Count(t => t.Kind == ClassTimeKind.Class) + 1}节",
            Start = "08:00",
            End = "08:45",
            Kind = ClassTimeKind.Class,
        };

        AppServices.TimeLayout.Add(time);
        _selectedTime = time;
        RefreshTimeline();
        SelectTime(time);
        RenderTimetable();
    }

    private void RemoveTime()
    {
        if (_selectedTime is null)
            return;

        AppServices.TimeLayout.Remove(_selectedTime);
        _selectedTime = null;
        RefreshTimeline();
        RenderTimetable();
    }

    private void MoveTime(int delta)
    {
        if (_selectedTime is null)
            return;

        AppServices.TimeLayout.Move(_selectedTime, delta);
        RefreshTimeline();
        RenderTimetable();
    }

    private void SaveTimeEdit()
    {
        if (_selectedTime is null)
            return;

        var start = TimeStartPicker.SelectedTime ?? TimeSpan.Zero;
        var end = TimeEndPicker.SelectedTime ?? TimeSpan.Zero;
        if (end <= start)
            return;

        var updated = new ClassTime
        {
            Name = string.IsNullOrWhiteSpace(TimeNameBox.Text) ? "时间点" : TimeNameBox.Text!,
            Start = start.ToString(@"hh\:mm"),
            End = end.ToString(@"hh\:mm"),
            Kind = TimeKindCombo.SelectedIndex == 1 ? ClassTimeKind.Break : ClassTimeKind.Class,
        };

        AppServices.TimeLayout.Update(_selectedTime, updated);
        RefreshTimeline();
        RenderTimetable();
    }

    private void ResetTimeLayout()
    {
        AppServices.TimeLayout.Clear();
        AppServices.TimeLayout.EnsureFromProfile(AppServices.Schedule.Profile);
        _selectedTime = null;
        RefreshTimeline();
        RenderTimetable();
    }

    // ==================== 科目（图5）====================

    private void RefreshSubjectTable()
    {
        SubjectTableGrid.Children.Clear();
        SubjectTableGrid.ColumnDefinitions.Clear();
        SubjectTableGrid.RowDefinitions.Clear();

        string[] headers = { "科目名", "简称", "户外课程", "科任老师" };
        double[] widths = { 1.4, 0.7, 0.8, 1.2 };

        for (var c = 0; c < headers.Length; c++)
            SubjectTableGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(widths[c], GridUnitType.Star)));

        SubjectTableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var c = 0; c < headers.Length; c++)
        {
            var header = new TextBlock
            {
                Text = headers[c],
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(10, 8),
                Foreground = Brushes.Gray,
            };
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, c);
            SubjectTableGrid.Children.Add(header);
        }

        var subjects = AppServices.Schedule.Profile.Subjects;
        for (var i = 0; i < subjects.Count; i++)
        {
            var subject = subjects[i];
            var isSelected = ReferenceEquals(subject, _currentSubject);

            SubjectTableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            string[] cells = { subject.Name, subject.SimplifiedName ?? "", subject.IsOutDoor ? "✓" : "", subject.Teacher ?? "" };

            for (var c = 0; c < cells.Length; c++)
            {
                var cell = new Border
                {
                    Background = isSelected ? CiPalette.SelectionBrush() : null,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock
                    {
                        Text = cells[c],
                        FontSize = 13,
                        Margin = new Thickness(10, 7),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = isSelected ? CiPalette.OnAccentBrush() : null,
                    },
                };

                cell.PointerPressed += (_, _) => SelectSubjectRow(subject);

                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, c);
                SubjectTableGrid.Children.Add(cell);
            }
        }
    }

    private void SelectSubjectRow(Subject subject)
    {
        _currentSubject = subject;
        _loading = true;

        SubjectNameBox.Text = subject.Name;
        SubjectSimplifiedBox.Text = subject.SimplifiedName ?? "";
        SubjectTeacherBox.Text = subject.Teacher ?? "";
        OutdoorCheck.IsChecked = subject.IsOutDoor;

        _loading = false;
        RefreshSubjectTable();
    }

    private void PushSubjectEdit()
    {
        if (_loading || _currentSubject is null)
            return;

        _currentSubject.Name = SubjectNameBox.Text ?? "";
        _currentSubject.SimplifiedName = string.IsNullOrWhiteSpace(SubjectSimplifiedBox.Text) ? null : SubjectSimplifiedBox.Text;
        _currentSubject.Teacher = string.IsNullOrWhiteSpace(SubjectTeacherBox.Text) ? null : SubjectTeacherBox.Text;
        _currentSubject.IsOutDoor = OutdoorCheck.IsChecked == true;

        RefreshSubjectTable();
        RefreshPalette();
        RefreshSubjectCombo();
        RenderTimetable();
    }

    private void AddSubject()
    {
        var subject = AppServices.Schedule.AddSubject("新科目");
        _currentSubject = subject;
        SelectSubjectRow(subject);
        RefreshPalette();
        RefreshSubjectCombo();
    }

    private void DeleteSubject()
    {
        if (_currentSubject is null)
            return;

        AppServices.Schedule.RemoveSubject(_currentSubject);
        _currentSubject = null;

        SubjectNameBox.Text = "";
        SubjectSimplifiedBox.Text = "";
        SubjectTeacherBox.Text = "";
        OutdoorCheck.IsChecked = false;

        RefreshSubjectTable();
        RefreshPalette();
        RefreshSubjectCombo();
        RenderTimetable();
    }

    // ==================== 顶部操作 ====================

    private async void OnOpenTimetable(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickTimetableAsync(this);
        if (path is null)
            return;

        AppServices.LoadTimetable(path);
        LoadAll();
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickSaveTimetableAsync(this);
        if (path is null)
            return;

        AppServices.Schedule.ExportTo(path);
        ProfileInfoText.Text = $"已导出到：{path}";
    }

    // ==================== 小工具 ====================

    private static TimeSpan ParseTime(string text) =>
        TimeSpan.TryParse(text, out var t) ? t : TimeSpan.Zero;

    private static string ToCses(TimeSpan t) => t.ToString(@"hh\:mm\:ss");

    private static int WeeksToIndex(string weeks) => weeks switch
    {
        "odd" => 1,
        "even" => 2,
        _ => 0,
    };

    private static string IndexToWeeks(int index) => index switch
    {
        1 => "odd",
        2 => "even",
        _ => "all",
    };
}
