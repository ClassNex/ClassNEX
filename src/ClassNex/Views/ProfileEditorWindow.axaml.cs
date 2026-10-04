using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.ViewModels;

namespace ClassNex.Views;

/// <summary>档案编辑器：课表 / 时间表 / 科目 / 调课 四个标签页（对应 ClassIsland 的档案编辑器）。</summary>
public partial class ProfileEditorWindow : Window
{
    private readonly ObservableCollection<SubjectItem> _subjects = new();
    private bool _loading;
    private SubjectItem? _current;

    public ProfileEditorWindow()
    {
        InitializeComponent();
        SubjectList.ItemsSource = _subjects;
        WireEvents();

        // 在 UI 与数据都就绪后再选中第一项（避免 XAML 初始化阶段的 re-entrancy）
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
        SubjectList.SelectionChanged += (_, _) => SelectSubject();

        AddSubjectButton.Click += (_, _) => AddSubject();
        DeleteSubjectButton.Click += (_, _) => DeleteSubject();
        OpenButton.Click += OnOpenTimetable;
        SaveButton.Click += (_, _) =>
        {
            AppServices.SaveDocument();
            RefreshInfo();
        };
        ReloadButton.Click += (_, _) =>
        {
            AppServices.ReloadDocument();
            LoadAll();
        };

        SubjectNameBox.TextChanged += (_, _) => PushEdit();
        SubjectSimplifiedBox.TextChanged += (_, _) => PushEdit();
        SubjectTeacherBox.TextChanged += (_, _) => PushEdit();
        SubjectRoomBox.TextChanged += (_, _) => PushEdit();
    }

    private void LoadAll()
    {
        _loading = true;

        _subjects.Clear();
        foreach (var s in AppServices.Document.Subjects)
            _subjects.Add(new SubjectItem(s));

        RefreshInfo();
        _loading = false;

        if (_subjects.Count > 0)
            SubjectList.SelectedIndex = 0;
        else
            SelectSubject();

        RenderTimetable();
        BuildTimeLayoutList();
    }

    private void RefreshInfo() => ProfileInfoText.Text = $"当前课表文件：{AppServices.TimetablePath}";

    private void RenderTimetable()
    {
        var parity = ParityCombo.SelectedIndex switch
        {
            1 => "odd",
            2 => "even",
            _ => "all",
        };
        TimetableGridRenderer.Render(TimetableGrid, AppServices.Document, parity);
    }

    private void BuildTimeLayoutList()
    {
        TimeLayoutList.Children.Clear();

        var slots = new SortedSet<(TimeSpan Start, TimeSpan End)>();
        foreach (var sch in AppServices.Document.Schedules)
        {
            foreach (var c in sch.Classes)
            {
                if (TimeSpan.TryParse(c.StartTime, out var st) && TimeSpan.TryParse(c.EndTime, out var et))
                    slots.Add((st, et));
            }
        }

        if (slots.Count == 0)
        {
            TimeLayoutList.Children.Add(new TextBlock { Text = "暂无时间表数据。" });
            return;
        }

        var index = 1;
        foreach (var (start, end) in slots)
        {
            var minutes = (int)(end - start).TotalMinutes;
            var text = $"第 {index} 节    {start.ToString(@"hh\:mm")} – {end.ToString(@"hh\:mm")}    （{minutes} 分钟）";

            TimeLayoutList.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14, 10),
                Child = new TextBlock { Text = text, FontSize = 14 },
            });
            index++;
        }
    }

    private void SelectSubject()
    {
        _loading = true;
        _current = SubjectList.SelectedItem as SubjectItem;
        SubjectEditor.IsEnabled = _current is not null;

        SubjectNameBox.Text = _current?.Name ?? "";
        SubjectSimplifiedBox.Text = _current?.SimplifiedName ?? "";
        SubjectTeacherBox.Text = _current?.Teacher ?? "";
        SubjectRoomBox.Text = _current?.Room ?? "";

        _loading = false;
    }

    private void PushEdit()
    {
        if (_loading || _current is null)
            return;

        _current.Name = SubjectNameBox.Text ?? "";
        _current.SimplifiedName = SubjectSimplifiedBox.Text ?? "";
        _current.Teacher = SubjectTeacherBox.Text ?? "";
        _current.Room = SubjectRoomBox.Text ?? "";

        AppServices.SaveDocument();
        RenderTimetable();
    }

    private void AddSubject()
    {
        var model = new CsesSubject { Name = "新科目" };
        AppServices.Document.Subjects.Add(model);

        var item = new SubjectItem(model);
        _subjects.Add(item);
        SubjectList.SelectedItem = item;

        AppServices.SaveDocument();
        RenderTimetable();
    }

    private void DeleteSubject()
    {
        if (_current is null)
            return;

        AppServices.Document.Subjects.Remove(_current.Model);
        _subjects.Remove(_current);
        _current = null;

        AppServices.SaveDocument();
        SelectSubject();
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
}
