using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;
using ClassNex.Controls;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using AvaloniaFluentUI.Controls;
using AvaloniaFluentUI.Windowing;

namespace ClassNex.Views;









public partial class ProfileEditorWindow : AppWindow
{
    private readonly List<string> _subjectNames = new();
    private readonly ObservableCollection<WeekRow> _weekRows = new();
    private readonly List<int> _pageHistory = new();

    private bool _loading;
    private bool _syncingPalette;
    private bool _syncingGrid;
    private ClassTime? _selectedTime;
    private Subject? _currentSubject;
    private CellTarget? _selectedCell;
    private DateTime _weekStart;
    private string _parity = "all";
    private int _pageIndex = -1;
    private Control? _currentPageView;
    private bool _navigatingBack;
    private bool _shellReady, _allowMica, _watchHighContrast, _discardOnClose, _confirmingClose;
    private Windows.UI.ViewManagement.UISettings? _effects;
    private Windows.UI.ViewManagement.AccessibilitySettings? _accessibility;

    private static readonly string[] PageNames =
        { "档案信息", "课表", "时间表", "科目", "调课" };

    public ProfileEditorWindow()
    {
        InitializeComponent();
        _shellReady = true;
        ProfileVersionText.Text = AppServices.ProductVersion;
        BackButton.IsVisible = false;

        
        ParityCombo.SelectedIndex = 0;
        AutoAdvanceCheck.IsChecked = true;
        var today = AppServices.ClassroomNow.Date;
        _weekStart = today.AddDays(-(int)today.DayOfWeek);
        WeekGrid.ItemsSource = _weekRows;

        AppServices.ManagedSettingsApplied += ReloadManagedProfile;
        Closed += (_, _) => AppServices.ManagedSettingsApplied -= ReloadManagedProfile;
        BindWorkspaceLayout();
        WireEvents();
        LoadAll();

        NavView.SelectedItem = NavView.MenuItems[1];
        if (_pageIndex < 0)
            SwitchPage();
    }

    
    
    
    
    
    
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (TryGetPlatformHandle()?.HandleDescriptor != "HWND") return;
        if (TitleBar is { } bar) bar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        AppWindow.SetAllowInteractionInTitleBar(this, true);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        try
        {
            _accessibility = new();
            _effects = new();
            RefreshBackdrop();
            _effects.AdvancedEffectsEnabledChanged += OnEffectsChanged;
            _effects.ColorValuesChanged += OnEffectsChanged;
            _accessibility.HighContrastChanged += OnEffectsChanged;
            _watchHighContrast = true;
        }
        catch (System.Runtime.InteropServices.COMException) { RefreshBackdrop(); }
    }

    private void OnEffectsChanged(object sender, object args) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshBackdrop);

    private void RefreshBackdrop()
    {
        if (_effects is null) return;
        try { _allowMica = _effects.AdvancedEffectsEnabled && _accessibility?.HighContrast == false; }
        catch (System.Runtime.InteropServices.COMException) { _allowMica = false; }
        TransparencyLevelHint = _allowMica ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None] : [WindowTransparencyLevel.None];
        EditorShell.Classes.Set("mica", _allowMica && ActualTransparencyLevel == WindowTransparencyLevel.Mica);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_shellReady && change.Property == ActualTransparencyLevelProperty)
            EditorShell.Classes.Set("mica", _allowMica && ActualTransparencyLevel == WindowTransparencyLevel.Mica);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_accessibility is not null)
        {
            if (_watchHighContrast) _accessibility.HighContrastChanged -= OnEffectsChanged;
            _accessibility = null;
        }
        if (_effects is not null)
        {
            _effects.AdvancedEffectsEnabledChanged -= OnEffectsChanged;
            _effects.ColorValuesChanged -= OnEffectsChanged;
            _effects = null;
        }
        base.OnClosed(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_discardOnClose || !PageSwap.HasPendingChanges || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown) return;
        e.Cancel = true;
        if (!_confirmingClose) _ = ConfirmCloseAsync();
    }

    private async Task ConfirmCloseAsync()
    {
        _confirmingClose = true;
        try { if (await ConfirmDiscardAsync()) { _discardOnClose = true; Close(); } }
        finally { _confirmingClose = false; }
    }

    private async Task<bool> ConfirmDiscardAsync() => await new ContentDialog
    {
        Title = "放弃未应用的调课草稿？", Content = "已应用的调课会保留。未应用的日期和课程修改不会保存。",
        PrimaryButtonText = "放弃草稿", CloseButtonText = "继续编辑", DefaultButton = ContentDialogButton.Close
    }.ShowAsync(this) == ContentDialogResult.Primary;

    
    public void SelectTab(int index)
    {
        var navIndex = Math.Clamp(index, -1, 3) + 1;
        if (navIndex >= NavView.MenuItems.Count)
            return;

        var item = NavView.MenuItems[navIndex];
        if (ReferenceEquals(NavView.SelectedItem, item))
            SwitchPage();
        else
            NavView.SelectedItem = item;
    }

    

    private void WireEvents()
    {
        NavView.SelectionChanged += (_, _) => SwitchPage();
        DeleteSelectedCourseButton.Click += (_, _) => DeleteSelectedCourse();

        
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
            if (e.Property == ToggleSwitch.IsCheckedProperty)
                RefreshPaletteHint();
        };

        
        
        SubjectPaletteList.AddHandler(InputElement.PointerReleasedEvent, OnPalettePointerReleased,
            RoutingStrategies.Tunnel);

        SaveNowButton.Click += OnSaveNow;

        
        if (AddTimeButton.Flyout is MenuFlyout add && add.Items is { Count: 4 } entries)
        {
            var kinds = new[] { ClassTimeKind.Class, ClassTimeKind.Break, ClassTimeKind.Separator, ClassTimeKind.Action };
            for (var i = 0; i < kinds.Length; i++)
                if (entries[i] is MenuItem command) { var kind = kinds[i]; command.Click += (_, _) => AddTime(kind); }
        }
        if (TimeOptionsButton.Flyout is MenuFlyout options && options.Items is { Count: 4 } commands)
        {
            _duplicateTime = (MenuItem)commands[0]!;
            _removeTime = (MenuItem)commands[1]!;
            _duplicateTime.Click += (_, _) => DuplicateTime();
            _removeTime.Click += (_, _) => RemoveTime();
            ((MenuItem)commands[3]!).Click += async (_, _) => await ConfirmRebuildTimeAsync();
        }
        MoveUpButton.Click += (_, _) => MoveTime(-1);
        MoveDownButton.Click += (_, _) => MoveTime(1);
        SaveTimeButton.Click += (_, _) => SaveTimeEdit();
        ClassMinutesBox.ValueChanged += (_, _) => SaveDefaultMinutes();
        BreakMinutesBox.ValueChanged += (_, _) => SaveDefaultMinutes();
        TimeKindCombo.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            var marker = TimeKindCombo.SelectedIndex >= 2;
            TimeEndPicker.IsEnabled = !marker;
            TimeNameBox.IsEnabled = TimeKindCombo.SelectedIndex != 2;
        };

        
        AddSubjectButton.Click += (_, _) => AddSubject();
        DeleteSubjectButton.Click += (_, _) => DeleteSubject();
        SaveSubjectsButton.Click += (_, _) =>
        {
            OnSaveNow(this, new RoutedEventArgs());
        };

        SubjectNameBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectSimplifiedBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectTeacherBox.TextChanged += (_, _) => PushSubjectEdit();
        OutdoorCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property == ToggleSwitch.IsCheckedProperty)
                PushSubjectEdit();
        };

        OpenButton.Click += OnOpenTimetable;
        ExportButton.Click += OnExport;
        ReloadButton.Click += OnReloadProfile;
    }

    

    private void ReloadManagedProfile()
    {
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { if (IsVisible) LoadAll(); }
        else Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsVisible) LoadAll(); });
    }

    private void LoadAll()
    {
        _loading = true;
        _selectedTime = null;
        _currentSubject = null;
        _selectedCell = null;

        ClearTimeEditor();
        ClearSubjectEditor();
        _loading = true;
        RefreshSubjectNames();
        RefreshPalette();
        RefreshSubjectTable();
        RefreshTimeline();
        RefreshInfo();
        RefreshNavigation();

        ClassMinutesBox.Value = AppServices.Settings.DefaultOnClassTimePointMinutes;
        BreakMinutesBox.Value = AppServices.Settings.DefaultBreakingTimePointMinutes;
        _loading = false;

        RenderTimetable();
        if (_pageIndex == 4) RefreshDayEditor();
    }

    private void SwitchPage()
    {
        var name = (NavView.SelectedItem as NavigationViewItem)?.Content as string ?? "";
        var index = Array.IndexOf(PageNames, name);
        if (index < 0)
            index = 1;
        if (index == _pageIndex)
            return;

        var previousPage = _currentPageView;


        if (!_navigatingBack && _pageIndex >= 0)
            _pageHistory.Add(_pageIndex);

        _pageIndex = index;
        BackButton.IsVisible = _pageHistory.Count > 0;
        if (index == 0)
            RefreshInfo();
        if (index == 4) RefreshDayEditor();
        BaseCommands.IsVisible = index != 4;
        var title = PageNames[index];

        var page = index switch
        {
            0 => (Control)PageProfile,
            1 => PageSchedule,
            2 => PageTime,
            3 => PageSubject,
            _ => PageSwap,
        };

        _currentPageView = page;
        if (previousPage is not null) previousPage.IsVisible = false;
        page.IsVisible = true;
        if (page is ScrollViewer scroll) scroll.Offset = Vector.Zero;
        PageTitleText.Text = title;
        if (previousPage is not null) WinUIPageTransitions.FadeIn(page);
    }

    private void NavigateBack()
    {
        if (_pageHistory.Count == 0)
            return;

        var target = _pageHistory[^1];
        _pageHistory.RemoveAt(_pageHistory.Count - 1);

        _navigatingBack = true;
        try
        {
            NavView.SelectedItem = NavView.MenuItems[target];
            if (_pageIndex != target)
                SwitchPage();
        }
        finally
        {
            _navigatingBack = false;
        }

        BackButton.IsVisible = _pageHistory.Count > 0;
    }

    private void OnNavigateBack(object? sender, RoutedEventArgs e) => NavigateBack();

    private void OnWorkspaceLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var index)) SelectTab(index);
    }

    private void RefreshDayEditor() => PageSwap.Bind(AppServices.Schedule, AppServices.LocalSchedule.TemporarySchedules, AppServices.Time, AppServices.TimeLayout, () => AppServices.ClassroomNow);

    private async void OnSaveNow(object? sender, RoutedEventArgs e)
    {
        if (_pageIndex == 4) { PageSwap.Apply(); return; }
        if (AppServices.LocalSchedule.TrySave(out var error)) ProfileInfoText.Text = $"已保存 · {AppServices.TimetableFileName}";
        else await new ContentDialog { Title = "基础课表未保存", Content = error, CloseButtonText = "关闭" }.ShowAsync(this);
    }

    private void OnReloadProfile(object? sender, RoutedEventArgs e)
    {
        AppServices.ReloadTimetable();
        LoadAll();
    }

    private void RefreshInfo()
    {
        var profile = AppServices.Schedule.Profile;
        var semester = AppServices.Settings.SemesterStartDate?.ToString("yyyy年M月d日") ?? "未设置";
        var courseCount = profile.Schedules.Sum(schedule => schedule.Classes.Count);

        ProfileInfoText.Text = AppServices.TimetableFileName;
        ToolTip.SetTip(ProfileInfoText, AppServices.TimetablePath);
        ProfileSummaryText.Text = "基础课表按星期和单双周循环。考试、补课或停课等单日安排请在「调课」中编辑。";
        ProfileNameValueText.Text = string.IsNullOrWhiteSpace(AppServices.TimetablePath) ? profile.Name : Path.GetFileNameWithoutExtension(AppServices.TimetablePath);
        ProfileFileValueText.Text = AppServices.TimetablePath;
        ProfileSemesterValueText.Text = semester;
        ProfileCourseCountText.Text = $"{courseCount} 节";
        ProfileSubjectCountText.Text = $"{profile.Subjects.Count} 门";
        ProfileTimePointCountText.Text = $"{AppServices.TimeLayout.Layout.Times.Count} 项";
    }

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
        NavDateText.Text = $"{_weekStart:M/d} – {_weekStart.AddDays(6):M/d}";
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
        DeleteSelectedCourseButton.IsEnabled = _selectedCell is { } selected && FindCourse(selected.EnableDay, _parity, selected.Start) is not null;
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
        var index = _selectedTime is null ? -1 : times.IndexOf(_selectedTime);
        if (_duplicateTime is not null) _duplicateTime.IsEnabled = index >= 0;
        if (_removeTime is not null) _removeTime.IsEnabled = index >= 0;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index + 1 < times.Count;
        if (times.Count == 0)
        {
            TimeAxisPanel.Children.Add(new TextBlock
            {
                Text = "还没有时间点。可添加时间点，或在更多操作中从课表重建。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
            });
            return;
        }

        foreach (var time in times)
        {
            var isBreak = time.Kind == ClassTimeKind.Break;
            var isMarker = time.Kind is ClassTimeKind.Separator or ClassTimeKind.Action;
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

            if (isMarker)
            {
                var action = time.Kind == ClassTimeKind.Action;
                var stripHost = new Border
                {
                    MinHeight = action ? 26 : 6,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, action ? 3 : 10, 0, 0),
                    CornerRadius = new CornerRadius(3),
                    Classes = { "timelineItem", action ? "activity" : "marker" },
                    BorderThickness = isSelected ? new Thickness(2) : new Thickness(0),
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                if (action)
                    stripHost.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(time.Name) ? "活动" : time.Name,
                        Margin = new Thickness(10, 4), TextWrapping = TextWrapping.Wrap };
                stripHost.PointerPressed += (_, _) => SelectTime(time);
                Grid.SetColumn(stripHost, 1);
                row.Children.Add(stripHost);
                TimeAxisPanel.Children.Add(row);
                continue;
            }

            var block = new Border
            {
                Classes = { "timelineItem", isBreak ? "break" : "lesson" },
                BorderThickness = isBreak ? new Thickness(isSelected ? 1 : 0) : new Thickness(3, isSelected ? 1 : 0, isSelected ? 1 : 0, isSelected ? 1 : 0),
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
                        },
                        new TextBlock
                        {
                            Text = $"{time.Name}　{DurationText(time)}",
                            FontSize = 11,
                            Opacity = 0.85,
                            TextWrapping = TextWrapping.Wrap,
                        },
                    },
                },
            };

            block.PointerPressed += (_, _) => SelectTime(time);
            Control timelineVisual = block;
            if (isSelected)
            {
                var ringHost = new Border
                {
                    BorderThickness = new Thickness(2),
                    Classes = { "timelineFocus" },
                    CornerRadius = new CornerRadius(5),
                    Child = block,
                };
                ringHost.PointerPressed += (_, _) => SelectTime(time);
                timelineVisual = ringHost;
            }

            Grid.SetColumn(timelineVisual, 1);
            row.Children.Add(timelineVisual);

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

        TimeFieldHint.IsVisible = false;
        TimeEditorTitle.Text = $"编辑时间点 · {time.Name}";
        TimeNameBox.Text = time.Name;
        TimeStartPicker.SelectedTime = TimeSpan.TryParse(time.Start, out var s) ? s : TimeSpan.Zero;
        TimeEndPicker.SelectedTime = TimeSpan.TryParse(time.End, out var e) ? e : TimeSpan.Zero;
        TimeKindCombo.SelectedIndex = time.Kind switch
        {
            ClassTimeKind.Break => 1,
            ClassTimeKind.Separator => 2,
            ClassTimeKind.Action => 3,
            _ => 0,
        };
        var marker = time.Kind is ClassTimeKind.Separator or ClassTimeKind.Action;
        TimeEndPicker.IsEnabled = !marker;
        TimeNameBox.IsEnabled = time.Kind != ClassTimeKind.Separator;
        TimeFields.IsEnabled = TimeFields.IsVisible = true;

        _loading = false;
        RefreshTimeline();
    }

    private void AddTime(ClassTimeKind kind)
    {
        var added = AppServices.TimeLayout.InsertNext(_selectedTime, kind, out var notice);
        if (added is null)
        {
            ShowTimeNotice(notice, true);
            return;
        }

        SelectTime(added);
        RenderTimetable();

        if (notice.Length > 0)
            ShowTimeNotice(notice, false);
    }

    private void DuplicateTime()
    {
        if (_selectedTime is null)
        {
            ShowTimeNotice("请先在时间轴上选中一个时间点。", true);
            return;
        }

        var copy = AppServices.TimeLayout.Duplicate(_selectedTime, out var notice);
        if (copy is null)
        {
            ShowTimeNotice(notice, true);
            return;
        }

        SelectTime(copy);
        RenderTimetable();

        ShowTimeNotice(notice.Length > 0 ? notice : "已创建时间点副本。", false);
    }

    private void RemoveTime()
    {
        if (_selectedTime is null)
            return;

        var index = AppServices.TimeLayout.Layout.Times.IndexOf(_selectedTime);
        AppServices.TimeLayout.Remove(_selectedTime);

        if (index > 0)
        {
            _selectedTime = AppServices.TimeLayout.Layout.Times[index - 1];
            SelectTime(_selectedTime);
        }
        else
        {
            _selectedTime = null;
            ClearTimeEditor();
            RefreshTimeline();
        }

        RenderTimetable();
    }

    private void ShowTimeNotice(string text, bool warning)
    {
        TimeInfoBar.Message = text;
        TimeInfoBar.Severity = warning
            ? AvaloniaFluentUI.Controls.InfoBarSeverity.Warning
            : AvaloniaFluentUI.Controls.InfoBarSeverity.Informational;
        TimeInfoBar.IsOpen = true;
    }

    private void ClearTimeEditor()
    {
        _loading = true;
        TimeFields.IsEnabled = TimeFields.IsVisible = false;
        TimeFieldHint.IsVisible = true;
        TimeEditorTitle.Text = "时间点";
        TimeNameBox.Text = "";
        TimeStartPicker.SelectedTime = null;
        TimeEndPicker.SelectedTime = null;
        TimeEndPicker.IsEnabled = true;
        TimeNameBox.IsEnabled = true;
        TimeKindCombo.SelectedIndex = 0;
        _loading = false;
    }

    private void SaveDefaultMinutes()
    {
        if (_loading || !double.IsFinite(ClassMinutesBox.Value) || !double.IsFinite(BreakMinutesBox.Value)) return;
        AppServices.Settings.DefaultOnClassTimePointMinutes =
            (int)Math.Clamp(ClassMinutesBox.Value, 0, 36000);
        AppServices.Settings.DefaultBreakingTimePointMinutes =
            (int)Math.Clamp(BreakMinutesBox.Value, 0, 36000);
        AppServices.SaveSettings();
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
        var kind = TimeKindCombo.SelectedIndex switch
        {
            1 => ClassTimeKind.Break,
            2 => ClassTimeKind.Separator,
            3 => ClassTimeKind.Action,
            _ => ClassTimeKind.Class,
        };
        var marker = kind is ClassTimeKind.Separator or ClassTimeKind.Action;
        if (!marker && end <= start)
        {
            ShowTimeNotice("结束时间必须晚于开始时间。", true);
            return;
        }

        var updated = new ClassTime
        {
            Name = kind == ClassTimeKind.Separator ? "分割线" : string.IsNullOrWhiteSpace(TimeNameBox.Text)
                ? kind == ClassTimeKind.Action ? "活动" : "时间点" : TimeNameBox.Text.Trim(),
            Start = start.ToString(@"hh\:mm"),
            End = (marker ? start : end).ToString(@"hh\:mm"),
            Kind = kind,
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
                var foreground = isSelected
                    ? CiPalette.OnAccentBrush()
                    : CiPalette.PrimaryTextBrush();
                var cell = new Border
                {
                    Background = isSelected ? CiPalette.SelectionBrush() : Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock
                    {
                        Text = cells[c],
                        FontSize = 13,
                        Margin = new Thickness(10, 7),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = foreground,
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

        SubjectFields.IsEnabled = DeleteSubjectButton.IsEnabled = true;
        SubjectFieldHint.IsVisible = false;
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

        ClearSubjectEditor();
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

        if (PageSwap.HasPendingChanges && !await ConfirmDiscardAsync()) return;
        try
        {
            AppServices.ImportTimetable(path);
            PageSwap.ForgetDrafts();
            LoadAll();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or YamlDotNet.Core.YamlException or ArgumentException)
        {
            await new ContentDialog { Title = "未能打开课表", Content = error.Message, CloseButtonText = "关闭" }.ShowAsync(this);
        }
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickSaveTimetableAsync(this);
        if (path is null)
            return;

        try
        {
            AppServices.Schedule.ExportTo(path);
            ProfileInfoText.Text = $"已导出到：{path}";
            ToolTip.SetTip(ProfileInfoText, path);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await new ContentDialog { Title = "未导出课表", Content = error.Message, CloseButtonText = "关闭" }.ShowAsync(this);
        }
    }
}
