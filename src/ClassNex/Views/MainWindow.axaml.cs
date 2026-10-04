using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.ViewModels;

namespace ClassNex.Views;

public partial class MainWindow : Window
{
    private static readonly string[] DayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };

    private static readonly Color[] Palette =
    {
        Color.Parse("#3B82F6"), // 蓝
        Color.Parse("#22C55E"), // 绿
        Color.Parse("#F59E0B"), // 琥珀
        Color.Parse("#EF4444"), // 红
        Color.Parse("#8B5CF6"), // 紫
        Color.Parse("#06B6D4"), // 青
        Color.Parse("#EC4899"), // 粉
        Color.Parse("#F97316"), // 橙
        Color.Parse("#14B8A6"), // 蓝绿
        Color.Parse("#6366F1"), // 靛蓝
    };

    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainWindowViewModel();
        DataContext = _vm;
        // 选中「全部」并触发首次渲染（放在 UI 与 _vm 都就绪之后，避免 XAML 初始化阶段的 re-entrancy）
        ParityCombo.SelectedIndex = 0;
    }

    private void OnParityChanged(object? sender, SelectionChangedEventArgs e) => RenderTimetable();

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var path = await PickTimetableFileAsync();
        if (path is null)
            return;

        try
        {
            _vm.ReloadFrom(path);
            RenderTimetable();
        }
        catch (Exception ex)
        {
            _vm.SourceDescription = "课表加载失败：" + ex.Message;
        }
    }

    private void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(
            path => { _vm.ReloadFrom(path); RenderTimetable(); },
            _vm.SourceDescription);
        _ = settings.ShowDialog(this);
    }

    private async Task<string?> PickTimetableFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开课表文件",
            AllowMultiple = false,
        };
        dialog.Filters.Add(new FileDialogFilter { Name = "CSES 课表", Extensions = new List<string> { "yaml", "yml" } });
        dialog.Filters.Add(new FileDialogFilter { Name = "所有文件", Extensions = new List<string> { "*" } });

        var result = await dialog.ShowAsync(this);
        return result is { Length: > 0 } ? result[0] : null;
    }

    // ---------- 课表渲染 ----------

    private void RenderTimetable()
    {
        TimetableGrid.Children.Clear();
        TimetableGrid.RowDefinitions.Clear();
        TimetableGrid.ColumnDefinitions.Clear();

        var doc = _vm.Document;
        if (doc is null || doc.Schedules.Count == 0)
        {
            TimetableGrid.Children.Add(BuildEmptyHint("暂无课表数据 — 点击右上角「打开课表…」载入 CSES 课表文件"));
            return;
        }

        var parity = ParityToCode(ParityCombo.SelectedIndex);

        var dayClasses = new Dictionary<int, List<CsesClass>>();
        for (var d = 1; d <= 7; d++)
            dayClasses[d] = new List<CsesClass>();

        foreach (var sched in doc.Schedules)
        {
            if (!MatchesParity(sched.Weeks, parity))
                continue;
            if (sched.EnableDay < 1 || sched.EnableDay > 7)
                continue;
            dayClasses[sched.EnableDay].AddRange(sched.Classes);
        }

        var boundaries = new SortedSet<TimeSpan>();
        var placed = new List<(int Day, CsesClass Class)>();
        foreach (var pair in dayClasses)
        {
            foreach (var cls in pair.Value)
            {
                if (!TryParseTime(cls.StartTime, out var s))
                    continue;
                if (!TryParseTime(cls.EndTime, out var e))
                    continue;
                boundaries.Add(s);
                boundaries.Add(e);
                placed.Add((pair.Key, cls));
            }
        }

        var bList = boundaries.ToList();
        if (bList.Count < 2)
        {
            TimetableGrid.Children.Add(BuildEmptyHint("当前周次下没有课程数据"));
            return;
        }

        var slotCount = bList.Count - 1;

        // 列：时间列 + 7 天
        TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var d = 0; d < 7; d++)
            TimetableGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));

        // 行：表头 + 时间段
        TimetableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < slotCount; i++)
            TimetableGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

        // 表头
        TimetableGrid.Children.Add(BuildHeaderCell("时间", 0, 0));
        for (var d = 0; d < 7; d++)
            TimetableGrid.Children.Add(BuildHeaderCell(DayNames[d], 0, d + 1));

        // 时间列
        for (var i = 0; i < slotCount; i++)
            TimetableGrid.Children.Add(BuildTimeCell(bList[i], i + 1));

        // 课程卡片
        var subjectLookup = doc.Subjects.ToDictionary(s => s.Name, s => s);
        foreach (var (day, cls) in placed)
        {
            TryParseTime(cls.StartTime, out var s);
            TryParseTime(cls.EndTime, out var e);
            var startIdx = bList.IndexOf(s);
            var endIdx = bList.IndexOf(e);
            if (startIdx < 0 || endIdx <= startIdx)
                continue;

            TimetableGrid.Children.Add(BuildClassCard(cls, subjectLookup, startIdx + 1, day, endIdx - startIdx));
        }
    }

    private static string ParityToCode(int index) => index switch
    {
        1 => "odd",
        2 => "even",
        _ => "all",
    };

    private static bool MatchesParity(string weeks, string parity) => parity switch
    {
        "odd" => weeks == "all" || weeks == "odd",
        "even" => weeks == "all" || weeks == "even",
        _ => true,
    };

    private static bool TryParseTime(string s, out TimeSpan ts) => TimeSpan.TryParse(s, out ts);

    private static string FormatTime(string hhmmss) =>
        TimeSpan.TryParse(hhmmss, out var ts) ? ts.ToString(@"hh\:mm") : hhmmss;

    private static Color SubjectColor(string name)
    {
        var h = 0;
        foreach (var c in name)
            h = (h * 31 + c) & 0x7fffffff;
        return Palette[h % Palette.Length];
    }

    private static Border BuildHeaderCell(string text, int row, int col)
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

    private static Border BuildTimeCell(TimeSpan t, int row)
    {
        var border = new Border
        {
            Child = new TextBlock
            {
                Text = t.ToString(@"hh\:mm"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 8, 0),
            },
        };
        Grid.SetRow(border, row);
        Grid.SetColumn(border, 0);
        return border;
    }

    private static Border BuildClassCard(CsesClass cls, Dictionary<string, CsesSubject> subjects, int row, int col, int span)
    {
        var name = cls.Subject;
        subjects.TryGetValue(name, out var subject);

        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(subject?.Teacher))
            detailParts.Add(subject.Teacher);
        if (!string.IsNullOrWhiteSpace(subject?.Room))
            detailParts.Add(subject.Room);
        var detail = detailParts.Count > 0 ? string.Join(" · ", detailParts) : null;

        var panel = new StackPanel { Spacing = 2 };

        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontWeight = FontWeight.SemiBold,
            FontSize = 14,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
        });

        if (detail is not null)
        {
            panel.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 11,
                Foreground = Brushes.White,
                Opacity = 0.92,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"{FormatTime(cls.StartTime)}–{FormatTime(cls.EndTime)}",
            FontSize = 11,
            Foreground = Brushes.White,
            Opacity = 0.88,
        });

        var border = new Border
        {
            Background = new SolidColorBrush(SubjectColor(name)),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(2, 1),
            Padding = new Thickness(10, 6),
            Child = panel,
        };

        Grid.SetRow(border, row);
        Grid.SetColumn(border, col);
        Grid.SetRowSpan(border, span);
        return border;
    }

    private static TextBlock BuildEmptyHint(string message) => new()
    {
        Text = message,
        FontSize = 15,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 48, 0, 0),
    };
}
