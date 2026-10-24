using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ClassNex.Controls;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using AvaloniaFluentUI.Windowing;

namespace ClassNex.Views;









public partial class ProfileEditorWindow : AppWindow
{
    private readonly List<string> _subjectNames = new();
    private readonly ObservableCollection<WeekRow> _weekRows = new();

    private bool _loading;
    private bool _syncingPalette;
    private bool _syncingGrid;
    private ClassTime? _selectedTime;
    private Subject? _currentSubject;
    private CellTarget? _selectedCell;
    private DateTime _weekStart;
    private string _parity = "all";

    public ProfileEditorWindow()
    {
        InitializeComponent();

        
        ParityCombo.SelectedIndex = 0;
        AutoAdvanceCheck.IsChecked = true;
        _weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
        WeekGrid.ItemsSource = _weekRows;

        BuildCommandBar();
        WireEvents();
        LoadAll();
    }

    
    
    
    
    
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            if (TitleBar is { } bar)
                bar.TitleBarHitTestType = TitleBarHitTestType.Complex;

            AppWindow.SetAllowInteractionInTitleBar(this, true);
        }
        catch
        {
            
        }

        try
        {
            if (!OperatingSystem.IsWindows())
                return;

            
            if (Environment.OSVersion.Version < new Version(10, 0, 22000))
                return;

            TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.Mica,
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.None,
            };
            Background = Brushes.Transparent;
        }
        catch
        {
            
        }
    }

    
    public void SelectTab(int index)
    {
        if (index >= 0 && index < Tabs.ItemCount)
            Tabs.SelectedIndex = index;
    }

    

    private void BuildCommandBar()
    {
        AddCommand("刷新", "\uE72C", () => { AppServices.ReloadTimetable(); LoadAll(); });
        AddCommand("删除", "\uE74D", DeleteSelectedCourse);
    }

    private void AddCommand(string label, string glyph, Action action)
    {
        var button = new AvaloniaFluentUI.Controls.CommandBarButton
        {
            Label = label,
            IconSource = new AvaloniaFluentUI.Controls.FontIconSource
            {
                Glyph = glyph,
                FontFamily = new Avalonia.Media.FontFamily("Segoe MDL2 Assets"),
            },
        };

        button.Click += (_, _) => action();
        ClassCommandBar.PrimaryCommands.Add(button);
    }

    private void WireEvents()
    {
        
        WeekGrid.SelectionChanged += (_, _) => UpdateSelectedCellFromGrid();
        WeekGrid.CurrentCellChanged += (_, _) => UpdateSelectedCellFromGrid();

        
        PrevWeekButton.Click += (_, _) => ShiftWeek(-1);
        NextWeekButton.Click += (_, _) => ShiftWeek(1);

        ParityCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            _parity = ParityCombo.SelectedIndex switch { 1 => "odd", 2 => "even", _ => "all" };
            _selectedCell = null;
            RefreshPalette();
            RenderTimetable();
        };

        AutoAdvanceCheck.PropertyChanged += (_, e) =>
        {
            if (e.Property == CheckBox.IsCheckedProperty)
                RefreshPaletteHint();
        };

        
        
        SubjectPaletteList.AddHandler(InputElement.PointerReleasedEvent, OnPalettePointerReleased,
            RoutingStrategies.Tunnel);

        SaveNowButton.Click += (_, _) =>
        {
            AppServices.Schedule.Save();
            PaletteHintText.Text = "课表已保存。";
        };

        
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

    

    private void LoadAll()
    {
        _loading = true;
        _selectedTime = null;
        _currentSubject = null;
        _selectedCell = null;

        RefreshSubjectNames();
        RefreshPalette();
        RefreshSubjectTable();
        RefreshTimeline();
        RefreshInfo();
        RefreshNavigation();

        _loading = false;

        RenderTimetable();
    }

    private void RefreshInfo() => ProfileInfoText.Text = $"当前课表文件：{AppServices.TimetablePath}";

    private void RefreshSubjectNames()
    {
        _subjectNames.Clear();
        _subjectNames.AddRange(AppServices.Schedule.Profile.Subjects.Select(s => s.Name));
    }

    

    private void ShiftWeek(int delta)
    {
        _weekStart = _weekStart.AddDays(7 * delta);
        _selectedCell = null;
        RefreshNavigation();
        RefreshPalette();
        RenderTimetable();
    }

    private void RefreshNavigation()
    {
        var monday = _weekStart.AddDays(1);
        NavDateText.Text = monday.ToString("yyyy/M/d");
        NavWeekText.Text = $"第{System.Globalization.ISOWeek.GetWeekOfYear(monday)}周";
    }

    

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

    

    
    private void RenderTimetable()
    {
        _syncingGrid = true;
        try
        {
            var rows = WeekRowsBuilder.Build(AppServices.Schedule.Profile, _parity);

            _weekRows.Clear();
            foreach (var row in rows)
                _weekRows.Add(row);

            UpdateWeekHeaders();
            SyncSelectionToGrid();
        }
        finally
        {
            _syncingGrid = false;
        }
    }

    
    private void UpdateWeekHeaders()
    {
        if (WeekGrid.Columns.Count < 8)
            return;

        WeekGrid.Columns[0].Header = "时间";

        for (var c = 0; c < 7; c++)
        {
            var date = _weekStart.AddDays(c);
            var dayName = TimetableGridBuilder.DayNames[WeekRowsBuilder.ColumnDays[c] - 1];

            WeekGrid.Columns[c + 1].Header = new StackPanel
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
            };
        }
    }

    
    private void SyncSelectionToGrid()
    {
        if (_selectedCell is not { } cell)
            return;

        var index = -1;
        for (var i = 0; i < _weekRows.Count; i++)
        {
            if (ClassTime.SameTime(_weekRows[i].StartText, ClassTime.ToShortTime(cell.Start)))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return;

        WeekGrid.SelectedIndex = index;

        var column = Array.IndexOf(WeekRowsBuilder.ColumnDays, cell.EnableDay);
        if (column >= 0 && column + 1 < WeekGrid.Columns.Count)
            WeekGrid.CurrentColumn = WeekGrid.Columns[column + 1];
    }

    
    private void UpdateSelectedCellFromGrid()
    {
        if (_syncingGrid)
            return;

        if (WeekGrid.SelectedItem is not WeekRow row)
        {
            _selectedCell = null;
            RefreshPalette();
            return;
        }

        var columnIndex = WeekGrid.CurrentColumn?.DisplayIndex ?? 1;
        if (columnIndex < 1 || columnIndex > 7)
            columnIndex = 1;

        var day = WeekRowsBuilder.ColumnDays[columnIndex - 1];
        _selectedCell = new CellTarget(day,
            ClassTime.ToCsesTime(row.StartText), ClassTime.ToCsesTime(row.EndText));

        RefreshPalette();
    }

    
    private void AssignSubject(Subject subject)
    {
        if (_selectedCell is not { } cell)
        {
            PaletteHintText.Text = "请先在课表里点选一个格子。";
            return;
        }

        var existing = FindCourse(cell.EnableDay, _parity, cell.Start);

        if (existing is not null)
            existing.Course.Subject = subject.Name;
        else
            AppServices.Schedule.AddCourse(cell.EnableDay, _parity, new Course
            {
                Subject = subject.Name,
                StartTime = cell.Start,
                EndTime = cell.End,
            });

        PaletteHintText.Text =
            $"已在 {TimetableGridBuilder.DayNames[cell.EnableDay - 1]} " +
            $"{ClassTime.ToShortTime(cell.Start)}-{ClassTime.ToShortTime(cell.End)} 排入「{subject.DisplayName}」。";

        _selectedCell = AutoAdvanceCheck.IsChecked == true ? NextCell(cell) : cell;

        RefreshPalette();
        RenderTimetable();
    }

    private void DeleteSelectedCourse()
    {
        if (_selectedCell is not { } cell)
        {
            PaletteHintText.Text = "请先在课表里点选一个有课程的格子。";
            return;
        }

        var existing = FindCourse(cell.EnableDay, _parity, cell.Start);
        if (existing is not null)
        {
            AppServices.Schedule.RemoveCourse(existing.EnableDay, existing.Weeks, existing.Course);
            PaletteHintText.Text =
                $"已删除 {TimetableGridBuilder.DayNames[cell.EnableDay - 1]} " +
                $"{ClassTime.ToShortTime(cell.Start)}-{ClassTime.ToShortTime(cell.End)} 的课程。";
        }
        else
        {
            PaletteHintText.Text = "这个格子没有课程。";
        }

        RefreshPalette();
        RenderTimetable();
    }

    
    private CellTarget? NextCell(CellTarget current)
    {
        var times = AppServices.TimeLayout.Layout.Times
            .Where(t => t.Kind == ClassTimeKind.Class)
            .Select(t => (Start: ClassTime.ToShortTime(t.Start), End: ClassTime.ToShortTime(t.End)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Start) && !string.IsNullOrWhiteSpace(x.End))
            .ToList();

        if (times.Count == 0)
            return null;

        var index = times.FindIndex(x => ClassTime.SameTime(x.Start, ClassTime.ToShortTime(current.Start)));
        if (index < 0)
            index = 0;

        if (index + 1 < times.Count)
            return new CellTarget(current.EnableDay,
                ClassTime.ToCsesTime(times[index + 1].Start), ClassTime.ToCsesTime(times[index + 1].End));

        var nextDay = current.EnableDay % 7 + 1;
        return new CellTarget(nextDay, ClassTime.ToCsesTime(times[0].Start), ClassTime.ToCsesTime(times[0].End));
    }

    

    
    
    
    
    private void OnPalettePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_syncingPalette)
            return;

        var subject = FindSubjectFrom(e.Source as Visual);
        if (subject is not null)
            AssignSubject(subject);
    }

    private static Subject? FindSubjectFrom(Visual? source)
    {
        while (source is not null)
        {
            if (source is Control { DataContext: Subject subject })
                return subject;

            source = source.GetVisualParent();
        }

        return null;
    }

    private void RefreshPalette()
    {
        
        if (!ReferenceEquals(SubjectPaletteList.ItemsSource, AppServices.Schedule.Profile.Subjects))
            SubjectPaletteList.ItemsSource = AppServices.Schedule.Profile.Subjects;

        
        Subject? current = null;
        if (_selectedCell is { } cell)
        {
            var existing = FindCourse(cell.EnableDay, _parity, cell.Start);
            current = existing is null
                ? null
                : AppServices.Schedule.Profile.FindSubject(existing.Course.Subject);
        }

        _syncingPalette = true;
        SubjectPaletteList.SelectedItem = current;
        _syncingPalette = false;

        RefreshPaletteHint();
    }

    private void RefreshPaletteHint()
    {
        if (_selectedCell is { } cell)
        {
            PaletteHintText.Text =
                $"已选 {TimetableGridBuilder.DayNames[cell.EnableDay - 1]} " +
                $"{ClassTime.ToShortTime(cell.Start)}-{ClassTime.ToShortTime(cell.End)}。点科目排课"
                + (AutoAdvanceCheck.IsChecked == true ? "，然后自动移到下一个课程。" : "。");
        }
        else
        {
            PaletteHintText.Text = "点课表里的一个格子选中，再点科目完成排课。";
        }
    }

    

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
        RefreshPalette();
        RenderTimetable();
    }

    private void AddSubject()
    {
        var subject = AppServices.Schedule.AddSubject("新科目");
        _currentSubject = subject;
        SelectSubjectRow(subject);
        RefreshSubjectNames();
        RefreshPalette();
        RenderTimetable();
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
        RefreshPalette();
        RenderTimetable();
    }

    

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
