using System.Collections.ObjectModel;
using System.ComponentModel;
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
/// 档案编辑器。课表页 1:1 复刻 CI（ClassIsland）档案编辑器的交互：
///   列表视图 DataGrid（启用/时间/科目下拉框，每行 = 一个节次）
///   + 右侧「编辑科目」面板 + 「选完科目自动移动到下一个课程」。
/// 编辑模型：选中一行 → 改科目下拉框或点右侧科目 → 立即生效（不再靠点格子）。
/// </summary>
public partial class ProfileEditorWindow : Window
{
    private readonly ObservableCollection<ClassGridRow> _classRows = new();
    private readonly List<string> _subjectNames = new();

    private bool _loading;
    private bool _updatingRow;
    private ClassTime? _selectedTime;
    private Subject? _currentSubject;
    private int _day = 1;
    private string _parity = "all";

    public ProfileEditorWindow()
    {
        InitializeComponent();

        // 初始化默认值要在事件接线之前，避免过早触发
        DayCombo.SelectedIndex = 0;
        ParityCombo.SelectedIndex = 0;
        AutoAdvanceCheck.IsChecked = true;
        ClassGrid.ItemsSource = _classRows;

        WireEvents();
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
        RefreshButton.Click += (_, _) => { AppServices.ReloadTimetable(); LoadAll(); };
        DeleteCourseButton.Click += (_, _) => DeleteSelectedCourse();

        DayCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _day = Math.Max(1, DayCombo.SelectedIndex + 1);
            RefreshClassGrid();
            RenderTimetable();
        };
        ParityCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _parity = ParityCombo.SelectedIndex switch { 1 => "odd", 2 => "even", _ => "all" };
            RefreshClassGrid();
            RenderTimetable();
        };
        AutoAdvanceCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == CheckBox.IsCheckedProperty)
                RefreshPaletteHint();
        };
        ClassGrid.SelectionChanged += (_, _) => RefreshPalette();

        // ---- 时间表 ----
        AddTimeButton.Click += (_, _) => AddTime();
        RemoveTimeButton.Click += (_, _) => RemoveTime();
        MoveUpButton.Click += (_, _) => MoveTime(-1);
        MoveDownButton.Click += (_, _) => MoveTime(1);
        ResetTimeButton.Click += (_, _) => ResetTimeLayout();
        SaveTimeButton.Click += (_, _) => SaveTimeEdit();

        // ---- 科目 ----
        AddSubjectButton.Click += (_, _) => AddSubject();
        DeleteSubjectButton.Click += (_, _) => DeleteSubject();
        SaveSubjectsButton.Click += (_, _) =>
        {
            AppServices.Schedule.Save();
            PaletteHintText.Text = "科目已保存到课表文件。";
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
        _selectedTime = null;
        _currentSubject = null;

        RefreshSubjectNames();
        RefreshPalette();
        RefreshSubjectTable();
        RefreshTimeline();
        RefreshInfo();

        _loading = false;

        RefreshClassGrid();
        RenderTimetable();
    }

    private void RefreshInfo() => ProfileInfoText.Text = $"当前课表文件：{AppServices.TimetablePath}";

    private void RefreshSubjectNames()
    {
        _subjectNames.Clear();
        _subjectNames.AddRange(AppServices.Schedule.Profile.Subjects.Select(s => s.Name));
    }

    // ==================== 课表（1:1 复刻 CI 列表视图） ====================

    private CourseRef? FindCourse(int day, string parity, string startCses)
    {
        foreach (var schedule in AppServices.Schedule.Profile.Schedules)
        {
            if (schedule.EnableDay != day)
                continue;
            if (!ScheduleProfile.MatchesParity(schedule.Weeks, parity))
                continue;

            foreach (var course in schedule.Classes)
            {
                if (ClassTime.SameTime(ClassTime.ToShortTime(course.StartTime),
                                       ClassTime.ToShortTime(startCses)))
                    return new CourseRef(schedule.EnableDay, schedule.Weeks, schedule, course);
            }
        }

        return null;
    }

    /// <summary>按当前所选星期与周次，重建列表视图的行（每行 = 一个上课时间点）。</summary>
    private void RefreshClassGrid()
    {
        var rows = new List<ClassGridRow>();

        foreach (var time in AppServices.TimeLayout.Layout.Times)
        {
            if (time.Kind != ClassTimeKind.Class)
                continue;
            if (!TimeSpan.TryParse(time.Start, out var ts) || !TimeSpan.TryParse(time.End, out var te))
                continue;
            if (te <= ts)
                continue;

            var start = ClassTime.ToShortTime(time.Start);
            var startCses = ClassTime.ToCsesTime(start);
            var existing = FindCourse(_day, _parity, startCses);

            rows.Add(new ClassGridRow(
                start,
                ClassTime.ToShortTime(time.End),
                _subjectNames,
                existing?.Course.Subject,
                existing is not null,
                OnRowSubjectChanged,
                OnRowEnabledChanged));
        }

        _classRows.Clear();
        foreach (var row in rows)
            _classRows.Add(row);

        RenderTimetable();
        RefreshPalette();
    }

    private void OnRowSubjectChanged(ClassGridRow row, string? subject)
    {
        if (_updatingRow)
            return;

        if (string.IsNullOrWhiteSpace(subject))
        {
            RemoveCourseAt(row);
            return;
        }

        SetCourseAt(row, subject!);
    }

    private void OnRowEnabledChanged(ClassGridRow row, bool enabled)
    {
        if (_updatingRow)
            return;

        if (!enabled)
        {
            RemoveCourseAt(row);
            return;
        }

        var subject = row.Subject;
        if (string.IsNullOrWhiteSpace(subject))
            subject = _subjectNames.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subject))
        {
            // 没有科目可选：回滚勾选
            row.IsEnabled = false;
            return;
        }

        SetCourseAt(row, subject!);
    }

    /// <summary>把科目写入所选节次（CI 编辑模型：改下拉框 / 点面板科目 → 立即生效）。</summary>
    private void SetCourseAt(ClassGridRow row, string subject)
    {
        _updatingRow = true;
        try
        {
            var existing = FindCourse(_day, _parity, ClassTime.ToCsesTime(row.Start));

            if (existing is not null)
                existing.Course.Subject = subject;
            else
                AppServices.Schedule.AddCourse(_day, _parity, new Course
                {
                    Subject = subject,
                    StartTime = ClassTime.ToCsesTime(row.Start),
                    EndTime = ClassTime.ToCsesTime(row.End),
                });

            if (!row.IsEnabled)
                row.IsEnabled = true;

            PaletteHintText.Text = $"已在 {TimetableGridBuilder.DayNames[_day - 1]} {row.TimeText} 排入「{subject}」。";

            if (AutoAdvanceCheck.IsChecked == true)
                MoveSelectionToNextRow(row);
            else
                RefreshPalette();

            RenderTimetable();
        }
        finally
        {
            _updatingRow = false;
        }
    }

    private void RemoveCourseAt(ClassGridRow row)
    {
        _updatingRow = true;
        try
        {
            var existing = FindCourse(_day, _parity, ClassTime.ToCsesTime(row.Start));
            if (existing is not null)
            {
                AppServices.Schedule.RemoveCourse(existing.EnableDay, existing.Weeks, existing.Course);
                PaletteHintText.Text = $"已删除 {TimetableGridBuilder.DayNames[_day - 1]} {row.TimeText} 的课程。";
                RenderTimetable();
            }
        }
        finally
        {
            _updatingRow = false;
        }
    }

    private void DeleteSelectedCourse()
    {
        if (ClassGrid.SelectedItem is not ClassGridRow row)
        {
            PaletteHintText.Text = "请先在左侧列表选中一行。";
            return;
        }

        if (row.IsEnabled)
            row.IsEnabled = false; // 触发 RemoveCourseAt
    }

    private void MoveSelectionToNextRow(ClassGridRow row)
    {
        var index = _classRows.IndexOf(row);
        if (index >= 0 && index + 1 < _classRows.Count)
            ClassGrid.SelectedIndex = index + 1;
        RefreshPalette();
    }

    // ==================== 编辑科目面板（CI 右侧） ====================

    private void RefreshPalette()
    {
        SubjectPalettePanel.Children.Clear();

        var selected = ClassGrid.SelectedItem as ClassGridRow;
        var selectedSubject = selected?.Subject;

        foreach (var subject in AppServices.Schedule.Profile.Subjects)
        {
            var picked = string.Equals(subject.Name, selectedSubject, StringComparison.Ordinal);
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

        RefreshPaletteHint();
    }

    private void RefreshPaletteHint()
    {
        var selected = ClassGrid.SelectedItem as ClassGridRow;

        PaletteHintText.Text = selected is null
            ? "先在左侧列表选中一个节次（行），再点科目完成排课。"
            : $"已选 {TimetableGridBuilder.DayNames[_day - 1]} {selected.TimeText}。点科目排课"
              + (AutoAdvanceCheck.IsChecked == true ? "，然后自动移到下一个课程。" : "。");
    }

    private void PickSubject(Subject subject)
    {
        if (ClassGrid.SelectedItem is not ClassGridRow row)
        {
            PaletteHintText.Text = "请先在左侧列表选中一个节次（行）。";
            return;
        }

        row.Subject = subject.Name;
    }

    // ==================== 周视图 ====================

    private void RenderTimetable()
    {
        TimetableGridBuilder.Render(
            TimetableGrid,
            AppServices.Schedule.Profile,
            _parity,
            null, null, null, null);
    }

    // ==================== 时间表 ====================

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
        TimeStartPicker.SelectedTime = TimeSpan.TryParse(time.Start, out var s) ? s : TimeSpan.Zero;
        TimeEndPicker.SelectedTime = TimeSpan.TryParse(time.End, out var e) ? e : TimeSpan.Zero;
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
        RefreshClassGrid();
    }

    private void RemoveTime()
    {
        if (_selectedTime is null)
            return;

        AppServices.TimeLayout.Remove(_selectedTime);
        _selectedTime = null;
        RefreshTimeline();
        RefreshClassGrid();
    }

    private void MoveTime(int delta)
    {
        if (_selectedTime is null)
            return;

        AppServices.TimeLayout.Move(_selectedTime, delta);
        RefreshTimeline();
        RefreshClassGrid();
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
        RefreshClassGrid();
    }

    private void ResetTimeLayout()
    {
        AppServices.TimeLayout.Clear();
        AppServices.TimeLayout.EnsureFromProfile(AppServices.Schedule.Profile);
        _selectedTime = null;
        RefreshTimeline();
        RefreshClassGrid();
    }

    // ==================== 科目 ====================

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
        RefreshSubjectNames();
        RefreshClassGrid();
        RefreshPalette();
    }

    private void AddSubject()
    {
        var subject = AppServices.Schedule.AddSubject("新科目");
        _currentSubject = subject;
        SelectSubjectRow(subject);
        RefreshSubjectNames();
        RefreshClassGrid();
        RefreshPalette();
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
        RefreshSubjectNames();
        RefreshClassGrid();
        RefreshPalette();
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
}

/// <summary>
/// 课表列表视图的一行（对应 CI DataGrid 的 ClassInfo 行）：
/// 一个上课时间点 × 当前星期/周次，含启用开关与科目下拉框。
/// </summary>
public sealed class ClassGridRow : INotifyPropertyChanged
{
    private readonly Action<ClassGridRow, string?> _onSubjectChanged;
    private readonly Action<ClassGridRow, bool> _onEnabledChanged;

    private string? _subject;
    private bool _isEnabled;

    public ClassGridRow(
        string start,
        string end,
        IReadOnlyList<string> subjects,
        string? subject,
        bool isEnabled,
        Action<ClassGridRow, string?> onSubjectChanged,
        Action<ClassGridRow, bool> onEnabledChanged)
    {
        Start = start;
        End = end;
        Subjects = subjects;
        _subject = subject;
        _isEnabled = isEnabled;
        _onSubjectChanged = onSubjectChanged;
        _onEnabledChanged = onEnabledChanged;
    }

    public string Start { get; }

    public string End { get; }

    /// <summary>科目下拉框的选项（与所有行共享同一列表实例）。</summary>
    public IReadOnlyList<string> Subjects { get; }

    public string TimeText => $"{Start}-{End}";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            _onEnabledChanged(this, value);
        }
    }

    public string? Subject
    {
        get => _subject;
        set
        {
            if (_subject == value)
                return;
            _subject = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Subject)));
            _onSubjectChanged(this, value);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
