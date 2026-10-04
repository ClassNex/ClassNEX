using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassNex.Controls;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.ViewModels;

namespace ClassNex.Views;

/// <summary>档案编辑器：课表（可编辑）/ 时间表（可编辑）/ 科目 / 调课。</summary>
public partial class ProfileEditorWindow : Window
{
    private readonly ObservableCollection<SubjectItem> _subjects = new();
    private readonly ObservableCollection<ClassTime> _times = new();

    private bool _loading;
    private SubjectItem? _currentSubject;
    private CourseRef? _selectedCourse;
    private ClassTime? _selectedTime;

    public ProfileEditorWindow()
    {
        InitializeComponent();
        SubjectList.ItemsSource = _subjects;
        TimeList.ItemsSource = _times;

        WireEvents();

        // 在 UI 与数据都就绪后再选中第一项，触发首次渲染
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

        AddCourseButton.Click += (_, _) => AddCourse();
        UpdateCourseButton.Click += (_, _) => UpdateCourse();
        DeleteCourseButton.Click += (_, _) => DeleteCourse();

        TimeList.SelectionChanged += (_, _) => SelectTime();
        AddTimeButton.Click += (_, _) => AddTime();
        RemoveTimeButton.Click += (_, _) => RemoveTime();
        MoveUpButton.Click += (_, _) => MoveTime(-1);
        MoveDownButton.Click += (_, _) => MoveTime(1);
        ResetTimeButton.Click += (_, _) => ResetTimeLayout();
        SaveTimeButton.Click += (_, _) => SaveTimeEdit();

        SubjectList.SelectionChanged += (_, _) => SelectSubject();
        AddSubjectButton.Click += (_, _) => AddSubject();
        DeleteSubjectButton.Click += (_, _) => DeleteSubject();
        SaveSubjectsButton.Click += (_, _) => SaveSubjects();

        SubjectNameBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectSimplifiedBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectTeacherBox.TextChanged += (_, _) => PushSubjectEdit();
        SubjectRoomBox.TextChanged += (_, _) => PushSubjectEdit();

        OpenButton.Click += OnOpenTimetable;
        ExportButton.Click += OnExport;
        ReloadButton.Click += (_, _) =>
        {
            AppServices.ReloadTimetable();
            LoadAll();
        };
    }

    // ==================== 加载 ====================

    private void LoadAll()
    {
        _loading = true;

        _subjects.Clear();
        foreach (var subject in AppServices.Schedule.Profile.Subjects)
            _subjects.Add(new SubjectItem(subject));

        _times.Clear();
        foreach (var time in AppServices.TimeLayout.Layout.Times)
            _times.Add(time);

        RefreshSubjectCombo();
        RefreshInfo();
        _loading = false;

        if (_subjects.Count > 0)
            SubjectList.SelectedIndex = 0;

        if (_times.Count > 0)
            TimeList.SelectedIndex = 0;

        _selectedCourse = null;
        _selectedTime = null;
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

    // ==================== 课表渲染 ====================

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
            PrefillFromEmptyCell);
    }

    private void ResetCourseEditor()
    {
        CourseEditorTitle.Text = "新增课程";
        CourseHintText.Text = "点击课表空格可快速填入星期与时间。";
    }

    private void SelectCourse(CourseRef courseRef)
    {
        _selectedCourse = courseRef;
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
    }

    private void PrefillFromEmptyCell(CellTarget target)
    {
        _selectedCourse = null;
        _loading = true;

        CourseEditorTitle.Text = "新增课程";
        CourseDayCombo.SelectedIndex = Math.Clamp(target.EnableDay - 1, 0, 6);
        CourseStartPicker.SelectedTime = ParseTime(target.Start);
        CourseEndPicker.SelectedTime = ParseTime(target.End);
        CourseWeeksCombo.SelectedIndex = 0;
        CourseHintText.Text =
            $"将在 {TimetableGridBuilder.DayNames[target.EnableDay - 1]} " +
            $"{ClassTime.ToShortTime(target.Start)}–{ClassTime.ToShortTime(target.End)} 新增课程，选好科目后点「新增」。";

        _loading = false;
        RenderTimetable();
    }

    // ==================== 课程增删改 ====================

    private void AddCourse()
    {
        if (CourseSubjectCombo.SelectedItem is not string subject || string.IsNullOrWhiteSpace(subject))
        {
            CourseHintText.Text = "请先在「科目」标签页添加科目。";
            return;
        }

        if (!TryReadTimes(out var start, out var end))
            return;

        var day = CourseDayCombo.SelectedIndex + 1;
        var weeks = IndexToWeeks(CourseWeeksCombo.SelectedIndex);

        AppServices.Schedule.AddCourse(day, weeks, new Course
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
            CourseHintText.Text = "请先点击课表中的一个课程卡片。";
            return;
        }

        if (CourseSubjectCombo.SelectedItem is not string subject || string.IsNullOrWhiteSpace(subject))
            return;

        if (!TryReadTimes(out var start, out var end))
            return;

        var day = CourseDayCombo.SelectedIndex + 1;
        var weeks = IndexToWeeks(CourseWeeksCombo.SelectedIndex);
        var updated = new Course
        {
            Subject = subject,
            StartTime = ToCses(start),
            EndTime = ToCses(end),
        };

        var original = _selectedCourse;
        if (original.EnableDay == day && original.Weeks == weeks)
        {
            AppServices.Schedule.UpdateCourse(day, weeks, original.Course, updated);
        }
        else
        {
            // 换了星期或周次：先删后加
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
            CourseHintText.Text = "请先点击课表中的一个课程卡片。";
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

    // ==================== 时间表 ====================

    private void RefreshTimeList()
    {
        _times.Clear();
        foreach (var time in AppServices.TimeLayout.Layout.Times)
            _times.Add(time);
    }

    private void SelectTime()
    {
        _selectedTime = TimeList.SelectedItem as ClassTime;
        if (_selectedTime is null)
            return;

        _loading = true;
        TimeNameBox.Text = _selectedTime.Name;
        TimeStartPicker.SelectedTime = ParseTime(_selectedTime.Start);
        TimeEndPicker.SelectedTime = ParseTime(_selectedTime.End);
        _loading = false;
    }

    private void AddTime()
    {
        var time = new ClassTime
        {
            Name = $"第{_times.Count + 1}节",
            Start = "08:00",
            End = "08:45",
        };

        AppServices.TimeLayout.Add(time);
        RefreshTimeList();
        TimeList.SelectedItem = time;
        RenderTimetable();
    }

    private void RemoveTime()
    {
        if (_selectedTime is null)
            return;

        AppServices.TimeLayout.Remove(_selectedTime);
        _selectedTime = null;
        RefreshTimeList();
        RenderTimetable();
    }

    private void MoveTime(int delta)
    {
        if (_selectedTime is null)
            return;

        AppServices.TimeLayout.Move(_selectedTime, delta);
        RefreshTimeList();
        TimeList.SelectedItem = _selectedTime;
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
            Name = string.IsNullOrWhiteSpace(TimeNameBox.Text) ? "节次" : TimeNameBox.Text!,
            Start = start.ToString(@"hh\:mm"),
            End = end.ToString(@"hh\:mm"),
            Kind = _selectedTime.Kind,
        };

        AppServices.TimeLayout.Update(_selectedTime, updated);
        RefreshTimeList();
        TimeList.SelectedItem = _selectedTime;
        RenderTimetable();
    }

    private void ResetTimeLayout()
    {
        AppServices.TimeLayout.Clear();
        AppServices.TimeLayout.EnsureFromProfile(AppServices.Schedule.Profile);
        RefreshTimeList();
        RenderTimetable();
    }

    // ==================== 科目 ====================

    private void SelectSubject()
    {
        _loading = true;
        _currentSubject = SubjectList.SelectedItem as SubjectItem;
        SubjectEditor.IsEnabled = _currentSubject is not null;

        SubjectNameBox.Text = _currentSubject?.Name ?? "";
        SubjectSimplifiedBox.Text = _currentSubject?.SimplifiedName ?? "";
        SubjectTeacherBox.Text = _currentSubject?.Teacher ?? "";
        SubjectRoomBox.Text = _currentSubject?.Room ?? "";

        _loading = false;
    }

    private void PushSubjectEdit()
    {
        if (_loading || _currentSubject is null)
            return;

        _currentSubject.Name = SubjectNameBox.Text ?? "";
        _currentSubject.SimplifiedName = SubjectSimplifiedBox.Text ?? "";
        _currentSubject.Teacher = SubjectTeacherBox.Text ?? "";
        _currentSubject.Room = SubjectRoomBox.Text ?? "";

        RefreshSubjectCombo();
        RenderTimetable();
    }

    private void AddSubject()
    {
        var subject = AppServices.Schedule.AddSubject("新科目");
        var item = new SubjectItem(subject);
        _subjects.Add(item);
        SubjectList.SelectedItem = item;

        RefreshSubjectCombo();
        RenderTimetable();
    }

    private void DeleteSubject()
    {
        if (_currentSubject is null)
            return;

        AppServices.Schedule.RemoveSubject(_currentSubject.Model);
        _subjects.Remove(_currentSubject);
        _currentSubject = null;

        SelectSubject();
        RefreshSubjectCombo();
        RenderTimetable();
    }

    private void SaveSubjects()
    {
        AppServices.Schedule.Save();
        CourseHintText.Text = "科目已保存到课表文件。";
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
