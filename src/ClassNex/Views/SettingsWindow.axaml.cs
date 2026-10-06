using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.ViewModels;
using AvaloniaFluentUI.Controls;

namespace ClassNex.Views;

/// <summary>
/// 应用设置。全程使用 FluentAvalonia（FluentUI）控件：
/// NavigationView（左侧导航）/ SettingsExpander（设置分组）/ ToggleSwitch（开关）/ FontIcon（图标）。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ObservableCollection<WidgetItem> _widgets = new();
    private readonly ObservableCollection<SearchEntry> _searchEntries = new();

    /// <summary>页面访问历史（供左上角返回箭头，照 Gallery 标题栏的 ← 行为）。</summary>
    private readonly List<int> _pageHistory = new();
    private int _lastPageIndex = -1;

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
    private static readonly string[] PageNames = { "基本", "外观", "窗口", "组件", "关于 ClassNEX", "账户" };

    /// <summary>设置项索引（供顶栏「查找设置」搜索；Title = 设置项，PageIndex = 所属页面）。</summary>
    private static readonly (string Title, int PageIndex)[] SearchIndex =
    {
        ("单周开始日期", 0),
        ("点击托盘图标行为", 0),
        ("当前课表文件", 0),
        ("重新加载课表", 0),
        ("主题（浅色 / 深色 / 跟随系统）", 1),
        ("主界面背景不透明度", 1),
        ("全局字号缩放", 1),
        ("主界面缩放", 1),
        ("组件排列方向", 2),
        ("鼠标穿透", 2),
        ("组件库（添加组件）", 3),
        ("恢复默认布局", 3),
        ("组件列表（上移 / 下移 / 删除）", 3),
        ("组件设置：启用该组件", 3),
        ("组件设置：字号缩放", 3),
        ("时钟组件显示秒", 3),
        ("自定义文本占位符", 3),
        ("版本信息", 4),
        ("用户名", 5),
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

        UserNameBox.Text = AppServices.Settings.UserName;
        EmailBox.Text = AppServices.Settings.Email;
        RefreshAccountName();

        SearchResults.ItemsSource = _searchEntries;

#if DEBUG
        // 自检：模拟「输入关键字 → 点搜索结果」全流程（设 CLASSNEX_VERIFY_SEARCH=1）
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SEARCH") == "1")
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var log = System.IO.Path.Combine(AppContext.BaseDirectory, "_verify.log");
                    SearchBox.Text = "组件";
                    System.IO.File.AppendAllText(log,
                        $"SEARCH 设置文本后 Text=「{SearchBox.Text}」命中 {_searchEntries.Count} 条\n");

                    if (_searchEntries.Count == 0)
                    {
                        UpdateSearchResults();
                        System.IO.File.AppendAllText(log,
                            $"SEARCH 手动重算后命中 {_searchEntries.Count} 条\n");
                    }

                    if (_searchEntries.Count > 0)
                    {
                        SearchResults.SelectedItem = _searchEntries[0];
                        System.IO.File.AppendAllText(log,
                            "SEARCH 已选中第一条结果（应触发导航且不闪退）\n");
                    }
                }
                catch (Exception ex)
                {
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(AppContext.BaseDirectory, "_verify.log"),
                        $"SEARCH 异常：{ex}\n");
                }
            }, DispatcherPriority.Background);
        }
#endif
    }

    /// <summary>账户区显示设置里的用户名与邮箱（照 FluentUI-Gallery 的账户卡：名称 + 邮箱）。</summary>
    private void RefreshAccountName()
    {
        var name = AppServices.Settings.UserName;
        var email = AppServices.Settings.Email;
        var displayName = string.IsNullOrWhiteSpace(name) ? "未设置用户名" : name.Trim();
        var displayEmail = string.IsNullOrWhiteSpace(email) ? "未设置邮箱" : email.Trim();

        AccountNameText.Text = displayName;
        AccountEmailText.Text = displayEmail;
        AccountPageNameText.Text = displayName;
        AccountPageEmailText.Text = displayEmail;

        // 头像描环：直接取软件强调色（用户要求「框框的取色按照软件取色来」）
        if (CiPalette.TryResource("AccentFillColorDefaultBrush", out var accentBrush) &&
            accentBrush is ISolidColorBrush solid)
        {
            AccountAvatarRing.BorderBrush = new SolidColorBrush(solid.Color);
            AccountPageAvatarRing.BorderBrush = new SolidColorBrush(solid.Color);
        }

        RefreshAvatarImages();
    }

    /// <summary>把颜色往白/黑方向混合（t=0 保持原色，t=1 变成 target）。</summary>
    private static Color Mix(Color c, Color target, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)Math.Clamp(c.R + (target.R - c.R) * t, 0, 255),
            (byte)Math.Clamp(c.G + (target.G - c.G) * t, 0, 255),
            (byte)Math.Clamp(c.B + (target.B - c.B) * t, 0, 255));
    }

    /// <summary>色相旋转（度）+ 饱和度倍数，用来从强调色派生出「第二色」。</summary>
    private static Color RotateHue(Color c, double degrees, double satScale)
    {
        var r = c.R / 255.0;
        var g = c.G / 255.0;
        var b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        double h = 0, s = 0;

        if (Math.Abs(max - min) > 1e-6)
        {
            var d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (Math.Abs(max - r) < 1e-6)
                h = (g - b) / d + (g < b ? 6 : 0);
            else if (Math.Abs(max - g) < 1e-6)
                h = (b - r) / d + 2;
            else
                h = (r - g) / d + 4;
            h /= 6;
        }

        h = (h + degrees / 360.0) % 1.0;
        if (h < 0)
            h += 1.0;
        s = Math.Clamp(s * satScale, 0, 1);

        double R = l, G = l, B = l;   // 灰阶兜底
        if (s > 1e-6)
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            R = HueToRgb(p, q, h + 1.0 / 3);
            G = HueToRgb(p, q, h);
            B = HueToRgb(p, q, h - 1.0 / 3);
        }

        return Color.FromRgb(
            (byte)Math.Clamp(R * 255, 0, 255),
            (byte)Math.Clamp(G * 255, 0, 255),
            (byte)Math.Clamp(B * 255, 0, 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
            t += 1;
        if (t > 1)
            t -= 1;
        if (t < 1.0 / 6)
            return p + (q - p) * 6 * t;
        if (t < 1.0 / 2)
            return q;
        if (t < 2.0 / 3)
            return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        NavigateTo("account");
    }

    /// <summary>头像文件路径：上传后复制到 data/avatar.png；不存在时用内置头像。</summary>
    private static string CustomAvatarPath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "data", "avatar.png");

    private const string DefaultAvatarUri = "avares://ClassNex/Assets/avatar.png";

    /// <summary>按当前头像状态刷新两处头像（左栏账户卡 + 账户页大图）。
    /// 用 Border.Background 的 ImageBrush 画图 —— Background 必被圆角裁剪，保证是正圆。</summary>
    private void RefreshAvatarImages()
    {
        try
        {
            Avalonia.Media.Imaging.Bitmap bitmap;
            if (System.IO.File.Exists(CustomAvatarPath))
            {
                using var stream = System.IO.File.OpenRead(CustomAvatarPath);
                bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }
            else
            {
                using var stream = AssetLoader.Open(new Uri(DefaultAvatarUri));
                bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }

            var brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };

            AccountAvatarRing.Background = brush;
            AccountPageAvatarRing.Background = brush;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[头像] 加载失败：{ex.Message}");
        }
    }

    /// <summary>更换头像：选图片 → 复制到 data/avatar.png → 立即刷新。</summary>
    private async void OnChangeAvatar(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "选择头像图片",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("图片")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.gif" },
                    },
                },
            });

            var file = files.FirstOrDefault();
            if (file is null)
                return;

            var dir = System.IO.Path.GetDirectoryName(CustomAvatarPath)!;
            System.IO.Directory.CreateDirectory(dir);

            await using (var src = await file.OpenReadAsync())
            await using (var dst = System.IO.File.Create(CustomAvatarPath))
            {
                await src.CopyToAsync(dst);
            }

            RefreshAvatarImages();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[头像] 更换失败：{ex.Message}");
        }
    }

    /// <summary>恢复默认头像：删除自定义文件并恢复内置头像。</summary>
    private void OnResetAvatar(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (System.IO.File.Exists(CustomAvatarPath))
                System.IO.File.Delete(CustomAvatarPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[头像] 删除失败：{ex.Message}");
        }

        RefreshAvatarImages();
    }

    /// <summary>左上角返回箭头：回到上一个页面。</summary>
    private void OnNavigateBack(object? sender, RoutedEventArgs e)
    {
        if (_pageHistory.Count == 0)
            return;

        var index = _pageHistory[^1];
        _pageHistory.RemoveAt(_pageHistory.Count - 1);
        if (index >= 0 && index < NavView.MenuItems.Count)
        {
            _lastPageIndex = index;
            NavView.SelectedItem = NavView.MenuItems[index];
        }
    }

    /// <summary>导航到指定页面：basic / appearance / window / widgets / about / account。</summary>
    public void NavigateTo(string page)
    {
        var index = page switch
        {
            "appearance" => 1,
            "window" => 2,
            "widgets" => 3,
            "about" => 4,
            _ => 0,
        };

        // 账户页不占导航位，从账户卡进入时直接切页
        if (page == "account")
        {
            PageGeneral.IsVisible = PageInterface.IsVisible = PageWindow.IsVisible =
                PageWidgets.IsVisible = PageAbout.IsVisible = false;
            PageAccount.IsVisible = true;
            PageTitleText.Text = "账户";
            return;
        }

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

        // ---- 账户（用户名 / 邮箱）----
        UserNameBox.TextChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.UserName = UserNameBox.Text ?? "";
            AppServices.SaveSettings();
            RefreshAccountName();
        };

        EmailBox.TextChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.Email = EmailBox.Text ?? "";
            AppServices.SaveSettings();
            RefreshAccountName();
        };

        // ---- 界面（主题改成下拉框：多选一的设置一律用 ComboBox，照 CI 的写法）----
        ThemeCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
                ApplyTheme();
        };

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
        ClickThroughSwitch.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyInterface();
        };

        // ---- 主界面组件 ----
        ResetWidgetButton.Click += (_, _) => ResetWidgets();
        WidgetList.SelectionChanged += (_, _) => SelectWidget();

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

        ThemeCombo.SelectedIndex = s.ThemeMode switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };

        OpacitySlider.Value = Math.Clamp(s.BackgroundOpacity, 0.1, 1);
        FontScaleSlider.Value = Math.Clamp(s.FontScale, 0.8, 1.6);
        MainWindowScaleSlider.Value = Math.Clamp(s.MainWindowScale, 0.6, 3.0);
        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x（CI 1.9）";
        OrientationCombo.SelectedIndex = s.Orientation == LayoutOrientation.Vertical ? 1 : 0;
        ClickThroughSwitch.IsChecked = s.IsClickThrough;

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
        // AvaloniaFluentUI 的 NavigationView.SelectedItem 是内部包装对象，
        // 不能靠 MenuItems.IndexOf(object) 找索引，改按 Content 字符串匹配。
        var name = (NavView.SelectedItem as NavigationViewItem)?.Content as string ?? "";
        var index = Array.IndexOf(PageNames, name);
        if (index < 0)
            index = 0;

        // 记录页面历史（返回箭头用）
        if (_lastPageIndex >= 0 && _lastPageIndex != index)
            _pageHistory.Add(_lastPageIndex);
        _lastPageIndex = index;

        PageGeneral.IsVisible = index == 0;
        PageInterface.IsVisible = index == 1;
        PageWindow.IsVisible = index == 2;
        PageWidgets.IsVisible = index == 3;
        PageAbout.IsVisible = index == 4;
        PageAccount.IsVisible = false;   // 账户页只从账户卡进入

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

        var pageIndex = entry.PageIndex;

        // 关键：这里正处在 ListBox.SelectionChanged 事件内部，
        // 直接清空 ItemsSource（_searchEntries）会让控件在处理选中后继续访问已移除的项 → 闪退。
        // 因此把导航与清理都推迟到本次事件处理结束之后。
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (pageIndex >= 0 && pageIndex < NavView.MenuItems.Count)
                    NavView.SelectedItem = NavView.MenuItems[pageIndex];
            }
            catch
            {
                // 忽略：导航失败不影响后续清理
            }

            SearchResultsPanel.IsVisible = false;
            _searchEntries.Clear();
            SearchBox.Text = "";
        }, DispatcherPriority.Background);
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

    private const string RepoUrl = "https://github.com/ClassNex/ClassNEX";

    private void OnOpenHomePage(object? sender, RoutedEventArgs e) => OpenUrl(RepoUrl);

    private void OnOpenIssues(object? sender, RoutedEventArgs e) => OpenUrl($"{RepoUrl}/issues");

    private void OnOpenLicense(object? sender, RoutedEventArgs e)
        => OpenUrl("https://www.gnu.org/licenses/gpl-3.0.html");

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // 忽略：无法打开浏览器
        }
    }

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
        // 下拉框：0=跟随系统 1=浅色 2=深色
        s.ThemeMode = ThemeCombo.SelectedIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };

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
        s.IsClickThrough = ClickThroughSwitch.IsChecked == true;

        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x（CI 1.9）";

        AppServices.SaveSettings();
    }

    // ==================== 主界面组件 ====================

    /// <summary>当前是否深色主题（Default 时跟随系统）。</summary>
    private static bool IsDarkTheme()
    {
        var variant = Application.Current?.ActualThemeVariant;
        if (variant == ThemeVariant.Dark)
            return true;
        if (variant == ThemeVariant.Light)
            return false;

        try
        {
            return Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant
                   == PlatformThemeVariant.Dark;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 组件卡片背景色。注意：不能走 TryFindResource(key) ——
    /// 它返回的是**浅色**变体的值（#b3ffffff），会在深色界面里画出白色卡片（用户反馈的「发白」）。
    /// </summary>
    private static IBrush CardBackground() =>
        new SolidColorBrush(IsDarkTheme() ? Color.Parse("#2E2E2E") : Color.Parse("#FBFBFB"));

    private static IBrush CardBorder() =>
        new SolidColorBrush(IsDarkTheme() ? Color.Parse("#3F3F3F") : Color.Parse("#E5E5E5"));

    /// <summary>组件库：每个组件类型一张卡片，双击添加。</summary>
    private void RefreshWidgetLibrary()
    {
        WidgetLibraryPanel.Children.Clear();

        foreach (var type in AppServices.Widgets.AvailableTypes)
        {
            // 卡片：定宽 + 文本区定宽，避免描述文字溢出到卡片外
            const double cardWidth = 268;
            const double textWidth = cardWidth - 28 - 32 - 12; // 左右内边距 + 图标 + 间距

            var card = new Border
            {
                Width = cardWidth,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(14, 10),
                CornerRadius = new CornerRadius(6),
                Background = CardBackground(),
                BorderBrush = CardBorder(),
                BorderThickness = new Thickness(1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children =
                    {
                        new AvaloniaFluentUI.Controls.FontIcon
                        {
                            Glyph = type.Glyph,
                            FontFamily = new Avalonia.Media.FontFamily("Segoe MDL2 Assets"),
                            FontSize = 32,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new StackPanel
                        {
                            Spacing = 3,
                            Width = textWidth,
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
                    },
                },
            };

            // 双击确认添加（用户要求：选中组件双击确认）
            card.DoubleTapped += (_, _) => AddWidget(type.Type);
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

    // 横向组件条「⋯」更多选项（CI 的 ComponentsOperationMenuFlyout）
    private void OnWidgetItemMore(object? sender, RoutedEventArgs e)
    {
        // 点「⋯」先把该项设为选中，后续菜单动作都作用于 _currentWidget
        if (sender is Control { DataContext: WidgetItem item })
            WidgetList.SelectedItem = item;
    }

    private void OnWidgetItemMoveUp(object? sender, RoutedEventArgs e) => MoveWidget(-1);

    private void OnWidgetItemMoveDown(object? sender, RoutedEventArgs e) => MoveWidget(1);

    private void OnWidgetItemDelete(object? sender, RoutedEventArgs e) => RemoveWidget();

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
