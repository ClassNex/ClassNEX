using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static ClassNex.WinUI.NativeSettingsKeys;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private sealed record HomeLink(string Key, string Title, string Description, string Group);
    private sealed record HomePreferences(string[]? Featured, string[]? Favorites);
    private static readonly HomeLink[] Links =
    [
        new("profile", "今日课表", "查看课程、时间表与作息。", "课表与桌面"),
        new("notification", "课堂提醒", "设置课前、上课与下课提醒。", "课表与桌面"),
        new("appearance", "外观", "调整主题、字号和桌面课表大小。", "课表与桌面"),
        new("window", "窗口", "设置排列方向、窗口置顶与鼠标穿透。", "课表与桌面"),
        new("widgets", "主界面组件", "添加、启用或调整桌面组件的顺序。", "课表与桌面"),
        new("account", "ClassNEX 账户", "查看账户资料和浏览器授权状态。", "账户与应用"),
        new("basic", "基本设置", "管理启动、托盘、学期和集控。", "账户与应用"),
        new("nex", "NEX Intelligence", "打开对话、配置助手权限和唤醒方式。", "账户与应用"),
        new("about", "关于 ClassNEX", "查看版本、开源许可和欢迎向导。", "账户与应用")
    ];
    private readonly List<string> _features = ["account", "profile", "appearance", "notification", "widgets"];
    private readonly HashSet<string> _favorites = [];
    private bool _preferencesRead, _favoritesOnly;
    private int _firstFeature;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _homeClock;

    private void SetHomeClock(bool enabled)
    {
        if (!enabled) { _homeClock?.Stop(); return; }
        if (_homeClock == null)
        {
            _homeClock = DispatcherQueue.CreateTimer();
            _homeClock.Interval = TimeSpan.FromMinutes(1);
            _homeClock.Tick += (_, _) =>
            {
                if (!_closed && CurrentPageKey == "home" && AppWindow.IsVisible) RefreshSettings();
            };
        }
        _homeClock.Start();
    }

    private void OpenLink(string key)
    {
        if (key == "profile") _bridge.Command(Profile);
        else NavigateTo(key);
    }

    private void HomePage()
    {
        LoadHomePreferences();
        var greeting = Target("HomeGreeting", "主页问候", Text("", "TitleTextBlockStyle"));
        _body.Children.Add(greeting);
        _bindings.Add(_ =>
        {
            var name = _bridge.Status(AccountName);
            var value = string.IsNullOrWhiteSpace(name) ? "Hello，请先登录" : $"{DateTime.Now.Hour switch { >= 5 and < 11 => "早上好", >= 11 and < 17 => "中午好", _ => "晚上好" }}，{name}";
            if (greeting.Text != value) greeting.Text = value;
        });
        var features = new Grid { ColumnSpacing = 10, MinHeight = 136 };
        var previous = new Button { Content = new FontIcon { Glyph = "\uE76B", FontSize = 14 } };
        var next = new Button { Content = new FontIcon { Glyph = "\uE76C", FontSize = 14 } };
        Identify(previous, "HomeFeaturePrevious", "上一组卡片");
        Identify(next, "HomeFeatureNext", "下一组卡片");
        var edit = new Button { Content = "编辑大卡片" };
        Identify(edit, "HomeFeaturesEdit", "编辑大卡片");
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        tools.Children.Add(previous); tools.Children.Add(next); tools.Children.Add(edit);
        Row("概览", "", tools);
        _body.Children.Add(features);
        var capacity = 1;
        var displayed = new List<(string Key, TextBlock Summary, TextBlock Detail)>();
        void UpdateValues(AppSettings s)
        {
            foreach (var (key, summary, detail) in displayed)
            {
                var (title, caption) = HomeStatus(key, s);
                if (summary.Text != title) summary.Text = title;
                if (detail.Text != caption) detail.Text = caption;
            }
        }
        void Arrange()
        {
            displayed.Clear();
            features.Children.Clear();
            features.ColumnDefinitions.Clear();
            _firstFeature = Math.Clamp(_firstFeature, 0, Math.Max(0, _features.Count - capacity));
            var keys = _features.Skip(_firstFeature).Take(capacity).ToArray();
            for (var i = 0; i < keys.Length; ++i)
            {
                var key = keys[i];
                var link = Links.Single(l => l.Key == key);
                features.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var stack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top };
                stack.Children.Add(Text(link.Title, "CaptionTextBlockStyle"));
                var summary = Text("", "SubtitleTextBlockStyle");
                var detail = Text("", "CaptionTextBlockStyle");
                stack.Children.Add(summary);
                stack.Children.Add(detail);
                displayed.Add((key, summary, detail));
                var tile = new Button { Content = stack, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(14) };
                Identify(tile, "Feature_" + key, link.Title);
                tile.Click += (_, _) => OpenLink(key);
                Grid.SetColumn(tile, i);
                features.Children.Add(tile);
            }
            previous.IsEnabled = _firstFeature > 0;
            next.IsEnabled = _firstFeature < _features.Count - capacity;
            UpdateValues(_bridge.ReadSettings());
        }
        previous.Click += (_, _) => { _firstFeature--; Arrange(); };
        next.Click += (_, _) => { _firstFeature++; Arrange(); };
        features.SizeChanged += (_, args) =>
        {
            var count = Math.Clamp((int)((args.NewSize.Width + 10) / 206), 1, 3);
            if (count != capacity) { capacity = count; Arrange(); }
        };
        edit.Click += (_, _) => ConfigureFeatures(edit, Arrange);
        Arrange();
        _bindings.Add(UpdateValues);

        var filter = new ComboBox { ItemsSource = new[] { "全部", "仅收藏" }, SelectedIndex = _favoritesOnly ? 1 : 0, MinWidth = 120 };
        Identify(filter, "HomeFilter", "筛选设置入口");
        Row("快捷入口", "", filter);
        var host = new StackPanel { Spacing = 12 };
        _body.Children.Add(host);
        var rows = new List<(HomeLink Link, Grid Row, ToggleButton Star)>();
        var groupBoxes = new Dictionary<string, StackPanel>();
        var empty = Text("还没有收藏的入口。");
        void ApplyFilter()
        {
            bool Shows(HomeLink link) => !_favoritesOnly || _favorites.Contains(link.Key);
            foreach (var (entry, row, star) in rows)
            {
                row.Visibility = Shows(entry) ? Visibility.Visible : Visibility.Collapsed;
                star.IsChecked = _favorites.Contains(entry.Key);
                star.Content = new FontIcon { Glyph = star.IsChecked == true ? "\uE735" : "\uE734", FontSize = 16 };
            }
            foreach (var (name, box) in groupBoxes) box.Visibility = rows.Any(x => x.Link.Group == name && Shows(x.Link)) ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = rows.Any(x => Shows(x.Link)) ? Visibility.Collapsed : Visibility.Visible;
        }
        foreach (var group in Links.GroupBy(x => x.Group))
        {
            var groupBox = new StackPanel { Spacing = 6 };
            groupBoxes.Add(group.Key, groupBox);
            groupBox.Children.Add(Text(group.Key, "SubtitleTextBlockStyle"));
            host.Children.Add(groupBox);
            foreach (var link in group)
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var stack = new StackPanel { Spacing = 1 };
                stack.Children.Add(Text(link.Title));
                stack.Children.Add(Text(link.Description, "CaptionTextBlockStyle"));
                var button = new Button { Content = stack, Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch };
                Identify(button, "Home_" + link.Key, link.Title);
                button.Click += (_, _) => OpenLink(link.Key);
                row.Children.Add(button);
                var star = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
                Identify(star, "Favorite_" + link.Key, "收藏：" + link.Title);
                ToolTipService.SetToolTip(star, "收藏 / 取消收藏");
                star.Click += (_, _) =>
                {
                    if (star.IsChecked == true) _favorites.Add(link.Key); else _favorites.Remove(link.Key);
                    SaveHomePreferences(); ApplyFilter();
                };
                rows.Add((link, row, star));
                Grid.SetColumn(star, 1);
                row.Children.Add(star);
                groupBox.Children.Add(row);
            }
        }
        host.Children.Add(empty);
        filter.SelectionChanged += (_, _) => { _favoritesOnly = filter.SelectedIndex == 1; ApplyFilter(); };
        ApplyFilter();
    }

    private (string, string) HomeStatus(string key, AppSettings s)
    {
        if (key == "profile")
        {
            var snapshot = _bridge.ReadOverlay();
            if (!snapshot.HasTimetable) return ("尚未配置课表", "打开档案编辑器");
            var today = snapshot.Today;
            var course = today.Current ?? today.Next ?? today.Slots.LastOrDefault();
            return course is null ? ("今天没有课程", Or(_bridge.Status(NativeSettingsKeys.Schedule), today.DateText))
                : (Or(course.Subject, course.DisplayName), $"{(today.Current != null ? "正在上课" : today.Next != null ? "下一节" : "最后一节")} · {course.TimeRange}");
        }
        return key switch
        {
            "account" => (Or(_bridge.Status(AccountName), "尚未登录"), Or(_bridge.Status(AccountEmail), "使用 ClassNEX ID 登录")),
            "notification" => (s.AllowNotification ? "课堂提醒已开启" : "课堂提醒已关闭", s.Notification.IsClassOnPreparingNotificationEnabled ? $"室内课提前 {s.Notification.ClassOnBreakReminderMinutes} 分钟提醒" : "课前准备提醒已关闭"),
            "appearance" => (s.ThemeMode switch { "dark" => "深色主题", "light" => "浅色主题", _ => "跟随系统主题" }, $"课表缩放 {s.MainWindowScale:0.##}× · 背景 {s.BackgroundOpacity:P0}"),
            "window" => (s.Orientation == LayoutOrientation.Vertical ? "纵向排列" : "横向排列", s.IsClickThrough ? "已启用鼠标穿透" : "已关闭鼠标穿透"),
            "widgets" => ($"已启用 {s.Widgets.Count(w => w.IsEnabled)} 个组件", $"布局中共有 {s.Widgets.Count} 个组件"),
            "nex" => (s.IsNexIntelligenceEnabled ? "助手已开启" : "助手已关闭", s.IsNexEdgeBarEnabled ? "已开启侧边唤醒条" : "侧边唤醒条已关闭"),
            "basic" => (s.RunAtStartup ? "开机自动启动" : "手动启动", $"单周起始日 {s.SingleWeekStartTime:yyyy/M/d}"),
            _ => (Or(_bridge.Status(NativeSettingsKeys.Version), "ClassNEX"), "查看项目与版本信息")
        };
    }

    private void ConfigureFeatures(FrameworkElement owner, Action refresh)
    {
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(6) };
        var toggles = new List<ToggleSwitch>();
        foreach (var link in Links)
        {
            var toggle = new ToggleSwitch { Header = link.Title, OnContent = "开", OffContent = "关", IsOn = _features.Contains(link.Key) };
            toggles.Add(toggle);
            toggle.IsEnabled = !toggle.IsOn || _features.Count > 1;
            toggle.Toggled += (_, _) =>
            {
                if (toggle.IsOn && !_features.Contains(link.Key)) _features.Add(link.Key);
                else if (!toggle.IsOn && _features.Count > 1) _features.Remove(link.Key);
                foreach (var item in toggles) item.IsEnabled = !item.IsOn || _features.Count > 1;
                SaveHomePreferences(); refresh();
            };
            panel.Children.Add(toggle);
        }
        new Flyout { Content = new ScrollViewer { Content = panel, MaxHeight = 320 } }.ShowAt(owner);
    }

    private static string HomePrefsPath => Path.Combine(SettingsService.DataDirectory, "SettingsHome.json");
    private void LoadHomePreferences()
    {
        if (_preferencesRead) return;
        _preferencesRead = true;
        try
        {
            if (!File.Exists(HomePrefsPath)) return;
            var prefs = JsonSerializer.Deserialize<HomePreferences>(File.ReadAllText(HomePrefsPath));
            if (prefs == null) return;
            static bool Known(string key) => Links.Any(x => x.Key == key);
            var saved = prefs.Featured?.Where(Known).Distinct().ToArray();
            if (saved is { Length: > 0 }) { _features.Clear(); _features.AddRange(saved); }
            if (prefs.Favorites != null) _favorites.UnionWith(prefs.Favorites.Where(Known));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void SaveHomePreferences()
    {
        try
        {
            SettingsService.EnsureDataDirectory();
            File.WriteAllText(HomePrefsPath + ".tmp", JsonSerializer.Serialize(new HomePreferences([.. _features], [.. _favorites])));
            File.Move(HomePrefsPath + ".tmp", HomePrefsPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void OverviewPage()
    {
        foreach (var d in Destinations.Where(d => d.Key is not ("settings" or "home") && (d.Key != "debug" || _bridge.ReadSettings().IsDebugOptionsEnabled)))
            ActionRow("GoTo_" + d.Key, d.Title, "打开", () => NavigateTo(d.Key), d.Description);
    }
}
