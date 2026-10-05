using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.ViewModels;
using FluentAvalonia.UI.Controls;

namespace ClassNex.Views;

/// <summary>
/// 应用设置。全程使用 FluentAvalonia（FluentUI）控件：
/// NavigationView（左侧导航）/ SettingsExpander（设置分组）/ ToggleSwitch（开关）/ FontIcon（图标）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<WidgetItem> _widgets = new();
    private readonly ObservableCollection<SearchEntry> _searchEntries = new();

    private bool _loading;
    private WidgetItem? _currentWidget;

    /// <summary>顶栏搜索的一条结果：设置项 + 所属页面。</summary>
    private sealed class SearchEntry
    {
        public string Title { get; init; } = "";

        public string PageName { get; init; } = "";

        public int PageIndex { get; init; }
    }

    /// <summary>页面名（顺序与 NavView.MenuItems 一致，对照 CI 的 SettingsPageInfo.Name）。</summary>
    private static readonly string[] PageNames = { "通用", "界面", "主界面组件", "课表", "关于" };

    /// <summary>设置项索引（供顶栏「查找设置」搜索；Title = 设置项，PageIndex = 所属页面）。</summary>
    private static readonly (string Title, int PageIndex)[] SearchIndex =
    {
        ("单周开始日期", 0),
        ("点击托盘图标行为", 0),
        ("主题（浅色 / 深色 / 跟随系统）", 1),
        ("主界面背景不透明度", 1),
        ("全局字号缩放", 1),
        ("主界面缩放", 1),
        ("组件排列方向", 1),
        ("鼠标穿透", 1),
        ("组件库（添加组件）", 2),
        ("恢复默认布局", 2),
        ("组件列表（上移 / 下移 / 删除）", 2),
        ("组件设置：启用该组件", 2),
        ("组件设置：字号缩放", 2),
        ("时钟组件显示秒", 2),
        ("自定义文本占位符", 2),
        ("当前课表文件", 3),
        ("重新加载课表", 3),
        ("版本信息", 4),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        WidgetList.ItemsSource = _widgets;

        InitShell();
        WireEvents();
        LoadFromSettings();
    }

    /// <summary>初始化外壳：顶栏版本号、导航栏账号块、顶栏搜索（对照 CI 的顶栏与用户给的系统设置截图）。</summary>
    private void InitShell()
    {
        var version = typeof(SettingsWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "26w41a";

        VersionText.Text = version;
        AboutVersionText.Text = $"版本 {version}";

        AccountNameText.Text = Environment.UserName;
        AccountPathItem.Header = $"课表文件：{System.IO.Path.GetFileName(AppServices.TimetablePath)}";

        SearchResults.ItemsSource = _searchEntries;
    }

    /// <summary>导航到指定页面：general / interface / widgets / schedule / about。</summary>
    public void NavigateTo(string page)
    {
        var index = page switch
        {
            "interface" => 1,
            "widgets" => 2,
            "schedule" => 3,
            "about" => 4,
            _ => 0,
        };

        if (index < NavView.MenuItems.Count)
            NavView.SelectedItem = NavView.MenuItems[index];
    }

    private void WireEvents()
    {
        // ---- 顶栏「查找设置」搜索 ----
        SearchBox.TextChanged += (_, _) => UpdateSearchResults();
        SearchResults.SelectionChanged += (_, _) => NavigateFromSearch();

        NavView.SelectionChanged += (_, _) => SwitchPage();

        // ---- 通用 ----
        SingleWeekStartPicker.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "SelectedDate")
                ApplyGeneral();
        };
        TrayBehaviorCombo.SelectionChanged += (_, _) => ApplyGeneral();

        // ---- 界面（ToggleSwitch 用属性名判断，避免依赖具体控件的静态属性）----
        ThemeSystem.PropertyChanged += (_, e) => ThemeChanged(e.Property.Name, ThemeSystem.IsChecked);
        ThemeLight.PropertyChanged += (_, e) => ThemeChanged(e.Property.Name, ThemeLight.IsChecked);
        ThemeDark.PropertyChanged += (_, e) => ThemeChanged(e.Property.Name, ThemeDark.IsChecked);

        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface();
        };
        FontScaleSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface();
        };
        MainWindowScaleSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface();
        };
        OrientationCombo.SelectionChanged += (_, _) => ApplyInterface();
        ClickThroughCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyInterface();
        };

        // ---- 主界面组件 ----
        ResetWidgetButton.Click += (_, _) => ResetWidgets();
        WidgetList.SelectionChanged += (_, _) => SelectWidget();
        RemoveWidgetButton.Click += (_, _) => RemoveWidget();
        MoveWidgetUpButton.Click += (_, _) => MoveWidget(-1);
        MoveWidgetDownButton.Click += (_, _) => MoveWidget(1);

        WidgetEnabledCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyWidgetEdit();
        };
        WidgetFontSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyWidgetEdit();
        };
        WidgetSecondsCheck.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyWidgetEdit();
        };
        WidgetTextBox.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Text")
                ApplyWidgetEdit();
        };

        // ---- 课表 ----
        OpenTimetableButton.Click += OnOpenTimetable;
        ReloadTimetableButton.Click += (_, _) =>
        {
            AppServices.ReloadTimetable();
            RefreshTimetableText();
        };
    }

    // ==================== 加载 ====================

    private void LoadFromSettings()
    {
        _loading = true;
        var s = AppServices.Settings;

        SingleWeekStartPicker.SelectedDate = new DateTimeOffset(s.SingleWeekStartTime);
        TrayBehaviorCombo.SelectedIndex = Math.Clamp(s.TrayClickBehavior, 0, 2);

        ThemeSystem.IsChecked = s.ThemeMode == "system";
        ThemeLight.IsChecked = s.ThemeMode == "light";
        ThemeDark.IsChecked = s.ThemeMode == "dark";

        OpacitySlider.Value = Math.Clamp(s.BackgroundOpacity, 0.1, 1);
        FontScaleSlider.Value = Math.Clamp(s.FontScale, 0.8, 1.6);
        MainWindowScaleSlider.Value = Math.Clamp(s.MainWindowScale, 0.6, 3.0);
        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x（CI 1.9）";
        OrientationCombo.SelectedIndex = s.Orientation == LayoutOrientation.Vertical ? 1 : 0;
        ClickThroughCheck.IsChecked = s.IsClickThrough;

        RefreshWidgetLibrary();
        RefreshWidgetList();

        if (_widgets.Count > 0)
            WidgetList.SelectedIndex = 0;

        RefreshTimetableText();

        _loading = false;

        NavView.SelectedItem = NavView.MenuItems[0];
        SwitchPage();
    }

    private void SwitchPage()
    {
        var index = NavView.SelectedItem is { } item && NavView.MenuItems.Contains(item)
            ? NavView.MenuItems.IndexOf(item)
            : 0;

        PageGeneral.IsVisible = index == 0;
        PageInterface.IsVisible = index == 1;
        PageWidgets.IsVisible = index == 2;
        PageSchedule.IsVisible = index == 3;
        PageAbout.IsVisible = index == 4;

        // 页面标题行（对照 CI 的 TitleContainer：页面名由外壳统一显示）
        PageTitleText.Text = PageNames[Math.Clamp(index, 0, PageNames.Length - 1)];
    }

    // ==================== 顶栏搜索（查找设置） ====================

    private void UpdateSearchResults()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        _searchEntries.Clear();

        if (query.Length > 0)
        {
            foreach (var (title, pageIndex) in SearchIndex)
            {
                if (title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || PageNames[pageIndex].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    _searchEntries.Add(new SearchEntry
                    {
                        Title = title,
                        PageName = PageNames[pageIndex],
                        PageIndex = pageIndex,
                    });
                }
            }
        }

        SearchResultsPanel.IsVisible = _searchEntries.Count > 0;
    }

    private void NavigateFromSearch()
    {
        if (SearchResults.SelectedItem is not SearchEntry entry)
            return;

        if (entry.PageIndex >= 0 && entry.PageIndex < NavView.MenuItems.Count)
            NavView.SelectedItem = NavView.MenuItems[entry.PageIndex];

        SearchResultsPanel.IsVisible = false;
        _searchEntries.Clear();
        SearchBox.Text = "";
    }

    // ==================== 更多选项 / 账号块 ====================

    private void OnOpenDataFolder(object? sender, RoutedEventArgs e)
        => OpenInExplorer(System.IO.Path.GetDirectoryName(AppServices.TimetablePath));

    private void OnOpenLogFile(object? sender, RoutedEventArgs e)
    {
        var log = System.IO.Path.Combine(AppContext.BaseDirectory, "_crash.log");
        OpenInExplorer(System.IO.File.Exists(log) ? log : AppContext.BaseDirectory);
    }

    private void OnShowAbout(object? sender, RoutedEventArgs e) => NavigateTo("about");

    private void OnOpenProfileEditor(object? sender, RoutedEventArgs e) => App.OpenProfileEditor(0);

    private void OnRestartApp(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        }
        catch
        {
            // 忽略：无法重启时仅退出
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private static void OpenInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // 忽略：资源管理器启动失败
        }
    }

    private void RefreshTimetableText() => TimetableFileText.Text = AppServices.TimetablePath;

    // ==================== 通用 / 界面 ====================

    private void ThemeChanged(string propertyName, bool? isChecked)
    {
        if (_loading || !isChecked.HasValue || propertyName != "IsChecked")
            return;

        ApplyTheme();
    }

    private void ApplyGeneral()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        if (SingleWeekStartPicker.SelectedDate is { } d)
            s.SingleWeekStartTime = d.Date;
        s.TrayClickBehavior = Math.Max(0, TrayBehaviorCombo.SelectedIndex);
        AppServices.SaveSettings();
    }

    private void ApplyTheme()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        s.ThemeMode = ThemeLight.IsChecked == true ? "light"
            : ThemeDark.IsChecked == true ? "dark"
            : "system";

        App.ApplyTheme(s.ThemeMode);
        AppServices.SaveSettings();
    }

    private void ApplyInterface()
    {
        if (_loading)
            return;

        var s = AppServices.Settings;
        s.BackgroundOpacity = OpacitySlider.Value;
        s.FontScale = FontScaleSlider.Value;
        s.MainWindowScale = MainWindowScaleSlider.Value;
        s.Orientation = OrientationCombo.SelectedIndex == 1
            ? LayoutOrientation.Vertical
            : LayoutOrientation.Horizontal;
        s.IsClickThrough = ClickThroughCheck.IsChecked == true;

        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x（CI 1.9）";

        AppServices.SaveSettings();
    }

    // ==================== 主界面组件 ====================

    /// <summary>组件库：每个组件类型一张卡片，点击即添加。</summary>
    private void RefreshWidgetLibrary()
    {
        WidgetLibraryPanel.Children.Clear();

        foreach (var type in AppServices.Widgets.AvailableTypes)
        {
            var card = new Border
            {
                Width = 190,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 10),
                CornerRadius = new CornerRadius(6),
                Background = CiPalette.SurfaceBrush("CardBackgroundFillColorSecondaryBrush", 0.25),
                BorderBrush = CiPalette.SurfaceBrush("CardStrokeColorDefaultBrush", 0.4),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = type.DisplayName,
                            FontSize = 14,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                        },
                        new TextBlock
                        {
                            Text = type.Description,
                            FontSize = 11,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                            Opacity = 0.7,
                        },
                    },
                },
            };

            card.PointerPressed += (_, _) => AddWidget(type.Type);
            WidgetLibraryPanel.Children.Add(card);
        }
    }

    private void RefreshWidgetList()
    {
        _widgets.Clear();
        foreach (var config in AppServices.Widgets.Widgets.OrderBy(w => w.Order))
            _widgets.Add(new WidgetItem(config));
    }

    private void SelectWidget()
    {
        _currentWidget = WidgetList.SelectedItem as WidgetItem;
        _loading = true;

        WidgetEditor.IsEnabled = _currentWidget is not null;

        if (_currentWidget is not null)
        {
            var config = _currentWidget.Config;
            WidgetTypeText.Text = $"{WidgetRegistry.DisplayNameOf(config.Type)} — {WidgetRegistry.DescriptionOf(config.Type)}";
            WidgetEnabledCheck.IsChecked = config.IsEnabled;
            WidgetFontSlider.Value = Math.Clamp(config.FontScale, 0.6, 2.0);
            WidgetFontValueText.Text = $"{config.FontScale:0.00}x";
            WidgetSecondsCheck.IsChecked = config.ShowSeconds;
            WidgetTextBox.Text = config.Text ?? "";
            WidgetSecondsCheck.IsEnabled = config.Type == "clock";
            WidgetTextBox.IsEnabled = config.Type == "text";
        }

        _loading = false;
    }

    private void ApplyWidgetEdit()
    {
        if (_loading || _currentWidget is null)
            return;

        var config = _currentWidget.Config;
        config.IsEnabled = WidgetEnabledCheck.IsChecked == true;
        config.FontScale = WidgetFontSlider.Value;
        config.ShowSeconds = WidgetSecondsCheck.IsChecked == true;
        config.Text = WidgetTextBox.Text;

        WidgetFontValueText.Text = $"{config.FontScale:0.00}x";
        _currentWidget.Refresh();

        AppServices.Widgets.Save();
    }

    private void AddWidget(string type)
    {
        var config = AppServices.Widgets.Add(type);
        RefreshWidgetList();
        WidgetList.SelectedItem = _widgets.FirstOrDefault(w => ReferenceEquals(w.Config, config));
    }

    private void RemoveWidget()
    {
        if (_currentWidget is null)
            return;

        AppServices.Widgets.Remove(_currentWidget.Config);
        _currentWidget = null;
        RefreshWidgetList();
        SelectWidget();
    }

    private void MoveWidget(int delta)
    {
        if (_currentWidget is null)
            return;

        AppServices.Widgets.Move(_currentWidget.Config, delta);
        var config = _currentWidget.Config;
        RefreshWidgetList();
        WidgetList.SelectedItem = _widgets.FirstOrDefault(w => ReferenceEquals(w.Config, config));
    }

    private void ResetWidgets()
    {
        AppServices.Widgets.ResetToDefault();
        _currentWidget = null;
        RefreshWidgetList();
        SelectWidget();
    }

    // ==================== 课表 ====================

    private async void OnOpenTimetable(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickTimetableAsync(this);
        if (path is null)
            return;

        AppServices.LoadTimetable(path);
        RefreshTimetableText();
    }
}
