using System.Text.Json;
using ClassNex.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private sealed record HomeLink(string Key, string Title, string Route, string Glyph, string Description, string? Target = null);
    private sealed record HomePreferences(string[]? Featured, string[]? Favorites);
    private static readonly HomeLink[] Links =
    [
        new("profile", "编辑课表", "profile", "\uE787", "打开独立的档案编辑器"),
        new("swap", "临时调课", "profile", "\uE8AB", "更改指定日期的安排，保留常规课表", "ProfileSwap"),
        new("widgets", "编排桌面组件", "widget-layout", "\uECA5", "添加、移除和排列桌面上的组件"),
        new("notification", "设置上下课提醒", "reminder-rules", "\uE823", "调整课前准备、上课和课间提醒"),
        new("basic", "启动与托盘", "startup", "\uE7E8", "设置开机自启和托盘菜单"),
        new("appearance", "调整课表外观", "appearance", "\uE790", "更改字号、透明度和缩放"),
        new("window", "调整窗口行为", "window", "\uE737", "管理置顶、鼠标穿透和排列方向"),
        new("account", "管理账户", "account", "\uE77B", "登录或切换 ClassNEX ID"),
        new("nex", "设置课堂助手", "nex-assistant", "\uE945", "打开 NEX 对话或调整助手权限"),
        new("about", "查看版本信息", "about", "\uE946", "查看 ClassNEX 版本和开源许可")
    ];
    private readonly List<string> _features = ["profile", "swap", "widgets", "notification"];
    private bool _preferencesRead;
    private Action? _refreshHomeCourse;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _homeClock;

    private void SetHomeClock(bool enabled)
    {
        if (!enabled || _refreshHomeCourse == null) { _homeClock?.Stop(); if (!enabled) _refreshHomeCourse = null; return; }
        if (_homeClock == null)
        {
            _homeClock = DispatcherQueue.CreateTimer();
            _homeClock.Interval = TimeSpan.FromMinutes(1);
            _homeClock.Tick += (_, _) => { if (!_closed && AppWindow.IsVisible) _refreshHomeCourse?.Invoke(); };
        }
        _homeClock.Start();
    }

    private void HomePage()
    {
        LoadHomePreferences();
        var heading = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(Text("快捷操作", "BodyLargeTextBlockStyle"));
        var edit = new Button { Content = "自定义", Style = ButtonStyle("SubtleButtonStyle") };
        Identify(edit, "HomeFeaturesEdit", "自定义快捷操作");
        Grid.SetColumn(edit, 1); heading.Children.Add(edit); _body.Children.Add(heading);
        var host = new ContentControl { IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        _body.Children.Add(host);
        void Arrange()
        {
            var tiles = new List<FrameworkElement>(_features.Count);
            foreach (var link in Links) _targets.Remove("Feature_" + link.Key);
            _refreshHomeCourse = null;
            foreach (var key in _features)
            {
                var link = Links.Single(item => item.Key == key);
                var caption = SettingCaption(link.Description);
                var tile = NavigationCard("Feature_" + key, link.Title, link.Glyph, caption, () => Navigate(link.Route, link.Target));
                tiles.Add(tile);
                if (key != "profile") continue;
                _refreshHomeCourse = () =>
                {
                    var day = _bridge.ReadOverlay();
                    var current = day.Today.Current;
                    var next = day.Today.Next;
                    var value = !day.HasTimetable ? "尚未设置课表，打开档案开始编排" : day.Today.IsEmpty ? "今天没有课程安排" :
                        current != null ? $"现在：{Or(current.Subject, current.DisplayName)} · {current.TimeRange}" :
                        next != null ? $"下一节：{Or(next.Subject, next.DisplayName)} · {next.TimeRange}" : "今天的安排已结束，查看或编辑课表";
                    if (caption.Text != value) { caption.Text = value; AutomationProperties.SetHelpText(tile, value); }
                };
                _refreshHomeCourse();
            }
            var grid = new Grid(); Identify(grid, "HomeCards", "快捷操作");
            SetCards(grid, tiles);
            host.Content = grid;
            SetHomeClock(true);
        }
        edit.Click += (_, _) => ConfigureFeatures(edit, Arrange);
        Arrange();
        _bindings.Add(_ => _refreshHomeCourse?.Invoke());
        Section("常用设置");
        var rows = new StackPanel { Spacing = 4 };
        var parent = _body;
        parent.Children.Add(rows); _body = rows;
        try
        {
            Switch("HomeReminders", "允许课堂提醒", s => s.AllowNotification, (s, value) => s.AllowNotification = value, "直接开启或暂停这台电脑上的课堂提醒。");
            ThemeChoice("HomeTheme", "应用主题");
        }
        finally { _body = parent; }
    }

    private void ConfigureFeatures(FrameworkElement owner, Action refresh)
    {
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(6) };
        var toggles = new List<ToggleSwitch>();
        foreach (var link in Links)
        {
            var toggle = new ToggleSwitch { OnContent = "开", OffContent = "关", IsOn = _features.Contains(link.Key) };
            Identify(toggle, "ShowFeature_" + link.Key, link.Title);
            toggles.Add(toggle);
            toggle.IsEnabled = !toggle.IsOn || _features.Count > 1;
            toggle.Toggled += (_, _) =>
            {
                if (toggle.IsOn && !_features.Contains(link.Key)) _features.Add(link.Key);
                else if (!toggle.IsOn && _features.Count > 1) _features.Remove(link.Key);
                foreach (var item in toggles) item.IsEnabled = !item.IsOn || _features.Count > 1;
                SaveHomePreferences(); refresh();
            };
            panel.Children.Add(MakeSettingRow(link.Title, "", toggle, false));
        }
        var available = owner.XamlRoot.Size;
        var scroll = new ScrollViewer { Content = panel, Width = Math.Clamp(available.Width - 64, 140, 316), MaxHeight = Math.Clamp(available.Height - 80, 100, 380),
            HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        new Flyout { Content = scroll }.ShowAt(owner);
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
            foreach (var key in prefs.Favorites ?? [])
                if (Known(key) && !_features.Contains(key)) _features.Add(key);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
    }

    private void SaveHomePreferences()
    {
        try
        {
            SettingsService.EnsureDataDirectory();
            File.WriteAllText(HomePrefsPath + ".tmp", JsonSerializer.Serialize(new HomePreferences([.. _features], null)));
            File.Move(HomePrefsPath + ".tmp", HomePrefsPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

}
