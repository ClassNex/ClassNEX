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
using AvaloniaFluentUI.Windowing;

namespace ClassNex.Views;

/// <summary>
/// 应用设置。全程使用 FluentAvalonia（FluentUI）控件：
/// NavigationView（左侧导航）/ SettingsExpander（设置分组）/ ToggleSwitch（开关）/ FontIcon（图标）。
/// </summary>
public partial class SettingsWindow : AppWindow
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
    private static readonly string[] PageNames = { "主页", "基本", "外观", "窗口", "组件", "NEX Intelligence", "提醒", "关于 ClassNEX", "调试", "账户" };

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

        // 账户卡形态跟随左栏宽度：展开(~263 DIP) → 完整卡；折叠成图标栏(~28 DIP) → 只留小圆头像。
        // 之前去可视树里找 NavigationView 内部的 SplitView 订阅它，**Loaded 时那个 SplitView 还没生成**，
        // 于是订阅没挂上、折叠态永远不生效（头像还是 52 宽 → 溢出侧边栏被右侧内容区盖住）。
        // 直接监听自己的宽度最稳：不依赖任何内部控件、不写任何导航状态。
        AccountPanel.SizeChanged += (_, e) => ApplyAccountCompact(e.NewSize.Width < 150);
    }

    // ==================== 账户卡：左栏折叠时收缩（纯视觉，不碰导航状态） ====================

    private bool _accountCompact;

    /// <summary>
    /// compact=true：内容框边距收紧（14→2，让框够宽装下头像）、头像 52→36、文字纯透明度淡出；
    /// compact=false：还原。
    /// 关键：**全程不动任何布局开关**（不切 IsVisible —— 那会在动画中途引起布局突变，就是一抖一抖的来源），
    /// 只改「头像尺寸 / 文字透明度」两个重绘级属性（都带 150ms 过渡）。
    /// </summary>
    private void ApplyAccountCompact(bool compact)
    {
        if (_accountCompact == compact)
            return;

        _accountCompact = compact;

        if (compact)
        {
            // 折叠态内容框实测只有 ~27 DIP 宽（WinUI 紧凑面板的内容宽度），
            // 所以头像必须 ≤26 DIP 才能真正装下 —— 36/32 都会溢出被右缘裁掉。取 24。
            // 左边距 11：框变成 11..46（宽 35），头像 11..35 落在里面不被裁，
            // 且中心在 23 DIP —— 正好是侧边栏图标列的中心（a11y 实测导航项中心 214px ≈ 23 DIP）。
            // 边距 11+2 = 13 → 框宽 48-13 = 35 DIP，所以头像放到 32 也装得下（中心仍取 23 → 左边距 7）。
            AccountPanel.Margin = new Thickness(7, 6, 2, 10);
            AccountShift.X = 0;
            AccountAvatarRing.Width = 32;
            AccountAvatarRing.Height = 32;
            AccountAvatarRing.CornerRadius = new CornerRadius(16);
            AccountAvatarRing.BorderThickness = new Thickness(2);
            AccountTextHost.Opacity = 0;
            AccountTextHost.IsHitTestVisible = false;
        }
        else
        {
            AccountPanel.Margin = new Thickness(14, 6, 6, 10);
            AccountShift.X = 0;
            AccountAvatarRing.Width = 52;
            AccountAvatarRing.Height = 52;
            AccountAvatarRing.CornerRadius = new CornerRadius(26);
            AccountAvatarRing.BorderThickness = new Thickness(3);
            AccountTextHost.Opacity = 1;
            AccountTextHost.IsHitTestVisible = true;
        }
    }

    /// <summary>账户卡的渲染级位移（XAML 里 RenderTransform 的 TranslateTransform，x:Name 不生成字段，这里取）。</summary>
    private TranslateTransform AccountShift => (TranslateTransform)AccountPanel.RenderTransform!;

    /// <summary>
    /// Mica 云母背景 + 沉浸式标题栏（照 CI 的 <c>MyWindow.OnLoaded</c>：
    /// Windows 11 21H2 / build 22000 及以上才启用，否则保持普通不透明背景）。
    /// 根元素已是 <c>AvaloniaFluentUI.Windowing.AppWindow</c>：客户区扩展到标题栏，
    /// 系统的最小化/最大化/关闭按钮直接画在右上角（顶栏第 4 列留了 140 宽避让）。
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // 顶栏在标题栏区域内，默认只用于拖动窗口 → 里面的按钮（返回 / 更多选项）收不到点击。
        // TitleBarHitTestType 默认 Simple = 整条标题栏只做边界判定；改成 Complex 后
        // 会用渲染器对可视元素做命中测试，标题栏里的控件即可交互（库文档原话：
        // "Use this if you're using something like a TabView or NavigationView"）。
        try
        {
            if (TitleBar is { } bar)
                bar.TitleBarHitTestType = TitleBarHitTestType.Complex;

            AppWindow.SetAllowInteractionInTitleBar(this, true);
        }
        catch
        {
            // 忽略：不支持时保持默认
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
            // 忽略：不支持 Mica 时保持普通背景
        }
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
            "home" => 0,
            "general" => 1,
            "appearance" => 2,
            "window" => 3,
            "widgets" => 4,
            "nex" => 5,
            "notification" => 6,
            "about" => 7,
            "debug" => 8,
            _ => 0,
        };

        // 账户页不占导航位，从账户卡进入时直接切页
        if (page == "account")
        {
            PageHome.IsVisible = PageGeneral.IsVisible = PageInterface.IsVisible = PageWindow.IsVisible =
                PageWidgets.IsVisible = PageNotification.IsVisible = PageAbout.IsVisible =
                PageDebug.IsVisible = PageNex.IsVisible = false;
            PageAccount.IsVisible = true;
            PageTitleText.Text = "账户";
            return;
        }

        // 按页面名找导航项再选中 —— 不能按 MenuItems[i] 取：主页与基本之间有一个分隔符，
        // 会把后面的索引整体顶偏一位（之前的隐性 bug）。
        var targetName = PageNames[Math.Clamp(index, 0, PageNames.Length - 1)];
        foreach (var item in NavView.MenuItems)
        {
            if (item is NavigationViewItem navItem &&
                string.Equals(navItem.Content as string, targetName, StringComparison.Ordinal))
            {
                NavView.SelectedItem = navItem;
                return;
            }
        }
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

        // ---- 课表 ----（「当前课表文件」一行已按用户要求移除，课表文件在档案编辑器里管理）

        // 调试页条目事件
        WireDebugPageEvents();

        // 组件库：双击添加（用户要求「选中组件双击确认」；单击只选中 → 左侧出现强调色小竖线）
        WidgetLibraryList.DoubleTapped += (_, _) =>
        {
            if (WidgetLibraryList.SelectedItem is WidgetTypeItem item)
                AddWidget(item.Type);
        };
#if DEBUG
        VerifyDialogHook();
#endif

        // 主页问候：每 60 秒刷新（只在主页可见时更新，避免无谓刷新）
        RefreshHomeGreeting();
        _homeGreetingTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _homeGreetingTimer.Tick += (_, _) =>
        {
            if (PageHome.IsVisible)
                RefreshHomeGreeting();
        };
        _homeGreetingTimer.Start();
    }

    // 主页（PageHome）目前按用户要求留空白，内容待设计。


    // ==================== 加载 ====================

    private void LoadFromSettings()
    {
        _loading = true;
        var s = AppServices.Settings;

        SingleWeekStartPicker.SelectedDate = s.SingleWeekStartTime;
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

        // 提醒设置（1:1 照 CI ClassNotificationSettings）
        LoadNotificationSettings();

        // 行为（开机自启 / Url 协议 / 学期开始时间周次 / 教学安全模式 / 启动加载界面）
        LoadBehaviorSettings();

        RefreshWidgetLibrary();
        RefreshWidgetList();

        if (_widgets.Count > 0)
            WidgetList.SelectedIndex = 0;

        RefreshDebugVisibility();

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

        PageHome.IsVisible = index == 0;
        PageGeneral.IsVisible = index == 1;
        PageInterface.IsVisible = index == 2;
        PageWindow.IsVisible = index == 3;
        PageWidgets.IsVisible = index == 4;
        PageNex.IsVisible = index == 5;
        PageNotification.IsVisible = index == 6;
        PageAbout.IsVisible = index == 7;
        PageDebug.IsVisible = index == 8;
        PageAccount.IsVisible = false;   // 账户页只从账户卡进入

        // 页面标题行（对照 CI 的 TitleContainer：页面名由外壳统一显示）
        PageTitleText.Text = PageNames[Math.Clamp(index, 0, PageNames.Length - 1)];

        if (index == 0)
            RefreshHomeGreeting();

        if (index == 5)
            RefreshNexPage();

        // 调试页里有 NEX 的接口配置（密钥只在这里填，普通界面不显示）
        if (index == 8)
            RefreshNexApiFields();
    }

    // ==================== NEX Intelligence（AI Agent 设置页） ====================

    private bool _nexEventsWired;
    private bool _nexLoading;

    /// <summary>NEX 设置页（权限 / 提示词 / 打开对话框）。接口配置在调试页，见 RefreshNexApiFields。</summary>
    private void RefreshNexPage()
    {
        var s = AppServices.Settings;

        if (!_nexEventsWired)
        {
            _nexEventsWired = true;

            NexOpenChatButton.Click += (_, _) => App.OpenNexChat();

            NexVoiceWakeCheck.IsCheckedChanged += (_, _) =>
            {
                if (_nexLoading)
                    return;

                var on = NexVoiceWakeCheck.IsChecked == true;
                AppServices.Settings.IsNexVoiceWakeEnabled = on;
                SettingsService.Save(AppServices.Settings);

                if (on)
                    AppServices.NexVoice?.Start();
                else
                    AppServices.NexVoice?.Stop();
            };

            NexGlowTestButton.Click += (_, _) =>
            {
                // 手动演示一次「跑马灯 + 浮窗翻页」：不需要麦克风也能看效果
                AppServices.NexVoice?.ShowGlow();
                AppServices.MainWindow?.ShowNexPanel("请讲", true);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    AppServices.NexVoice?.ShowGlow();     // 再次点亮以刷新，然后收起
                    AppServices.MainWindow?.HideNexPanel();
                };
                timer.Start();
            };

            AppServices.NexVoice?.StatusChanged += status =>
                Dispatcher.UIThread.Post(() => NexVoiceStatusText.Text = status);

            NexSystemPromptBox.TextChanged += (_, _) => SaveNexSettings();
            NexPermissionCombo.SelectionChanged += (_, _) => SaveNexSettings();

            // 调试页里的接口配置
            NexBaseUrlBox.TextChanged += (_, _) => SaveNexApiFields();
            NexApiKeyBox.TextChanged += (_, _) => SaveNexApiFields();
            NexModelCombo.SelectionChanged += (_, _) => SaveNexApiFields();
            NexModelCombo.LostFocus += (_, _) => SaveNexApiFields();
            NexFetchModelsButton.Click += async (_, _) => await FetchModelsAsync();
            NexTestButton.Click += async (_, _) => await TestNexConnectionAsync();
            NexResetButton.Click += (_, _) =>
            {
                AppServices.Settings.NexApiBaseUrl = "";
                AppServices.Settings.NexApiKey = "";
                AppServices.Settings.NexModel = "";
                SettingsService.Save(AppServices.Settings);
                RefreshNexApiFields();
                NexStatusText.Text = "已恢复内置接口。";
            };
        }

        _nexLoading = true;
        try
        {
            NexSystemPromptBox.Text = s.NexSystemPrompt;
            NexPermissionCombo.SelectedIndex = (int)Services.NexIntelligence.NexPermissionNames.FromKey(s.NexPermission);
            NexVoiceWakeCheck.IsChecked = s.IsNexVoiceWakeEnabled;
            NexVoiceStatusText.Text = AppServices.NexVoice?.IsRunning == true ? "正在听「嘿 NEX」…" : "未启用";
            UpdateNexApiStateText();
        }
        finally
        {
            _nexLoading = false;
        }
    }

    /// <summary>主页上那行「当前接口」状态：只说明用的是内置还是自定义，不显示密钥明文。</summary>
    private void UpdateNexApiStateText()
    {
        var s = AppServices.Settings;
        var usingBuiltIn = Services.NexIntelligence.NexConfig.IsUsingBuiltInKey(s);
        var model = Services.NexIntelligence.NexConfig.ResolveModel(s);
        NexApiStateText.Text = usingBuiltIn
            ? $"内置接口 · {model}"
            : $"自定义接口 · {model}";
        NexApiStateItem.Description = usingBuiltIn
            ? "正在使用内置接口（密钥不出现在界面上）"
            : "正在使用你自己填写的接口配置";
    }

    /// <summary>填调试页里的接口三件套（密钥框永远留空，不回显内置密钥）。</summary>
    private void RefreshNexApiFields()
    {
        var s = AppServices.Settings;

        _nexLoading = true;
        try
        {
            NexBaseUrlBox.Text = s.NexApiBaseUrl;
            NexApiKeyBox.Text = s.NexApiKey;              // 只可能是用户自己填的，内置密钥不回显
            NexModelCombo.ItemsSource = new[] { Services.NexIntelligence.NexConfig.ResolveModel(s) };
            NexModelCombo.SelectedIndex = 0;
            NexModelCombo.Text = Services.NexIntelligence.NexConfig.ResolveModel(s);
        }
        finally
        {
            _nexLoading = false;
        }
    }

    private void SaveNexApiFields()
    {
        if (_nexLoading)
            return;

        var s = AppServices.Settings;
        s.NexApiBaseUrl = NexBaseUrlBox.Text?.Trim() ?? "";
        s.NexApiKey = NexApiKeyBox.Text?.Trim() ?? "";

        var model = (NexModelCombo.SelectedItem as string) ?? NexModelCombo.Text?.Trim();
        if (string.IsNullOrWhiteSpace(model) || model == Services.NexIntelligence.NexConfig.BuiltInModel)
            s.NexModel = "";                              // 等于内置就存空，保持「留空=内置」
        else
            s.NexModel = model;

        SettingsService.Save(s);
        UpdateNexApiStateText();
    }

    private void SaveNexSettings()
    {
        if (_nexLoading)
            return;

        var s = AppServices.Settings;
        s.NexSystemPrompt = NexSystemPromptBox.Text ?? "";
        s.NexPermission = (NexPermissionCombo.SelectedIndex switch
        {
            1 => "workspace",
            2 => "full",
            _ => "readonly",
        });

        SettingsService.Save(s);
    }

    /// <summary>从接口拉模型列表填进下拉框（用当前生效的地址与密钥）。</summary>
    private async Task FetchModelsAsync()
    {
        SaveNexApiFields();
        NexStatusText.Text = "正在拉取模型列表…";

        try
        {
            var s = AppServices.Settings;
            var models = await Services.NexIntelligence.NexModelCatalog.FetchAsync(
                Services.NexIntelligence.NexConfig.ResolveBaseUrl(s),
                Services.NexIntelligence.NexConfig.ResolveApiKey(s));

            NexModelCombo.ItemsSource = models;
            var current = Services.NexIntelligence.NexConfig.ResolveModel(s);
            NexModelCombo.SelectedItem = models.Contains(current) ? current : models.FirstOrDefault();
            NexStatusText.Text = $"拉到 {models.Count} 个模型。";
        }
        catch (Exception ex)
        {
            NexStatusText.Text = "拉取失败：" + ex.Message;
        }
    }

    /// <summary>测一次最小请求，确认地址/密钥/模型可用。</summary>
    private async Task TestNexConnectionAsync()
    {
        SaveNexApiFields();
        NexStatusText.Text = "正在测试…";

        try
        {
            var s = AppServices.Settings;
            var reply = await Services.NexIntelligence.NexModelCatalog.TestAsync(
                Services.NexIntelligence.NexConfig.ResolveBaseUrl(s),
                Services.NexIntelligence.NexConfig.ResolveApiKey(s),
                Services.NexIntelligence.NexConfig.ResolveModel(s));

            NexStatusText.Text = "连接正常，模型回复：" + reply;
        }
        catch (Exception ex)
        {
            NexStatusText.Text = "测试失败：" + ex.Message;
        }
    }

    // ==================== 主页（照 FluentUI Gallery 首页：问候 + 横幅 + 分组卡片） ====================

    private DispatcherTimer? _homeGreetingTimer;

    /// <summary>按当前时间给问候语（早上好 / 中午好 / 晚上好），并刷新主页各卡片。</summary>
    private void RefreshHomeGreeting()
    {
        var name = string.IsNullOrWhiteSpace(AppServices.Settings.UserName)
            ? "同学"
            : AppServices.Settings.UserName.Trim();

        var now = DateTime.Now;
        var greeting = now.Hour switch
        {
            >= 5 and < 11 => "早上好",
            >= 11 and < 17 => "中午好",
            _ => "晚上好",
        };

        HomeGreetingText.Text = $"{greeting}，{name}";

        var slots = AppServices.Time.GetTodaySlots(AppServices.Schedule.Profile, now);
        var current = AppServices.Time.GetCurrentCourse(slots, now.TimeOfDay);
        var next = AppServices.Time.GetNextCourse(slots, now.TimeOfDay);
        var countdown = AppServices.Time.GetCountdownText(slots, now.TimeOfDay);
        HomeSubText.Text = $"{now:yyyy年M月d日 dddd} · 今日 {slots.Count} 节课"
            + (current is null && next is null ? "" : $" · {countdown}");

        // ---- 今天 ----
        HomeTodayPanel.Children.Clear();
        HomeTodayPanel.Children.Add(HomeCard("\uE736", "当前课程",
            current is null ? "现在没有课" : $"{current.DisplayName}  {current.TimeRange}"
                + (string.IsNullOrWhiteSpace(current.Detail) ? "" : $"\n{current.Detail}"), null));
        HomeTodayPanel.Children.Add(HomeCard("\uE76C", "下一节课",
            next is null ? "今天没有后续课程了" : $"{next.DisplayName}  {next.TimeRange}"
                + (string.IsNullOrWhiteSpace(next.Detail) ? "" : $"\n{next.Detail}"), null));
        HomeTodayPanel.Children.Add(HomeCard("\uE787", "今日课程", $"{slots.Count} 节", null));
        HomeTodayPanel.Children.Add(HomeCard("\uE916", "倒计时", countdown, null));

        // ---- 快捷操作 ----
        HomeActionsPanel.Children.Clear();
        HomeActionsPanel.Children.Add(HomeCard("\uE70F", "档案编辑器", "编辑课表与时间表",
            () => App.OpenProfileEditor(0)));
        HomeActionsPanel.Children.Add(HomeCard("\uE8B7", "数据目录", SettingsService.DataDirectory,
            () => OnOpenDataFolder(null, new RoutedEventArgs())));
        HomeActionsPanel.Children.Add(HomeCard("\uE7C3", "导出诊断数据", "设置 + 日志 + 版本信息",
            () => OnExportDiagnostics(null, new RoutedEventArgs())));
        HomeActionsPanel.Children.Add(HomeCard("\uE713", "应用设置", "打开「基本」页",
            () => NavigateTo("general")));

        // ---- 状态 ----
        var s = AppServices.Settings;
        HomeStatusPanel.Children.Clear();
        HomeStatusPanel.Children.Add(HomeCard("\uEA8F", "通知",
            s.AllowNotification ? "已开启" : "已关闭", () => NavigateTo("notification")));
        HomeStatusPanel.Children.Add(HomeCard("\uE945", "提醒效果",
            s.AllowNotificationEffect ? "水波纹已开启" : "水波纹已关闭", () => NavigateTo("notification")));
        HomeStatusPanel.Children.Add(HomeCard("\uE71D", "主界面组件",
            $"{s.Widgets.Count(w => w.IsEnabled)} 个已启用", () => NavigateTo("widgets")));
        HomeStatusPanel.Children.Add(HomeCard("\uE946", "应用版本",
            VersionText.Text ?? "?", () => NavigateTo("about")));
    }

    /// <summary>主页卡片（图标 + 标题 + 描述；有点击动作时鼠标变手形）。样式见 XAML 的 Border.HomeCard。</summary>
    private static Border HomeCard(string glyph, string title, string subtitle, Action? onClick)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new AvaloniaFluentUI.Controls.FontIcon
                {
                    Glyph = glyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 24,
                    HorizontalAlignment = HorizontalAlignment.Left,
                },
                new TextBlock { Text = title, FontSize = 15 },
                new TextBlock
                {
                    Text = subtitle,
                    FontSize = 12,
                    Opacity = 0.7,
                    TextWrapping = TextWrapping.Wrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                },
            },
        };

        var card = new Border { Child = panel };
        card.Classes.Add("HomeCard");

        if (onClick is not null)
        {
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.PointerPressed += (_, _) => onClick();
        }

        return card;
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
        => OpenInExplorer(SettingsService.DataDirectory);

    private void OnOpenLogsFolder(object? sender, RoutedEventArgs e)
    {
        SettingsService.EnsureDataDirectory();
        OpenInExplorer(SettingsService.LogsDirectory);
    }

    private void OnOpenAppFolder(object? sender, RoutedEventArgs e)
        => OpenInExplorer(AppContext.BaseDirectory);

    // ---- 创建快捷方式（对照 CI 的「创建快捷方式…」子菜单） ----

    private void OnCreateDesktopShortcut(object? sender, RoutedEventArgs e)
        => CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

    private void OnCreateStartMenuShortcut(object? sender, RoutedEventArgs e)
        => CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs));

    private static void CreateShortcut(string dir)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || string.IsNullOrWhiteSpace(dir))
                return;

            Directory.CreateDirectory(dir);

            // WScript.Shell 只在 Windows 上存在（CA1416）：显式判平台，避免分析器告警
            if (!OperatingSystem.IsWindows())
                return;

            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
                return;

            dynamic shell = Activator.CreateInstance(type)!;
            dynamic link = shell.CreateShortcut(System.IO.Path.Combine(dir, "ClassNEX.lnk"));
            link.TargetPath = exe;
            link.WorkingDirectory = AppContext.BaseDirectory;
            link.Save();
        }
        catch
        {
            // 忽略：无法创建快捷方式
        }
    }

    /// <summary>导出诊断数据（对照 CI 的「导出诊断数据」）：桌面生成一个含设置、日志、版本信息与系统信息的文件夹。</summary>
    private void OnExportDiagnostics(object? sender, RoutedEventArgs e)
    {
        try
        {
            SettingsService.EnsureDataDirectory();

            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"ClassNEX-诊断数据-{DateTime.Now:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(dir);

            if (File.Exists(SettingsService.SettingsPath))
                File.Copy(SettingsService.SettingsPath, System.IO.Path.Combine(dir, "Settings.json"), true);

            if (Directory.Exists(SettingsService.LogsDirectory))
            {
                foreach (var file in Directory.GetFiles(SettingsService.LogsDirectory))
                    File.Copy(file, System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file)), true);
            }

            var version = typeof(App).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            File.WriteAllText(System.IO.Path.Combine(dir, "版本信息.txt"),
                $"ClassNEX {version}{Environment.NewLine}" +
                $"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
                $"应用目录：{AppContext.BaseDirectory}{Environment.NewLine}" +
                $"数据目录：{SettingsService.DataDirectory}{Environment.NewLine}" +
                $"系统：{Environment.OSVersion}{Environment.NewLine}" +
                $".NET：{Environment.Version}{Environment.NewLine}");

            OpenInExplorer(dir);
        }
        catch
        {
            // 忽略：导出失败
        }
    }

    private void OnOpenLogFile(object? sender, RoutedEventArgs e)
    {
        SettingsService.EnsureDataDirectory();
        OpenInExplorer(System.IO.File.Exists(SettingsService.CrashLogPath)
            ? SettingsService.CrashLogPath
            : SettingsService.LogsDirectory);
    }

    private void OnShowAbout(object? sender, RoutedEventArgs e) => NavigateTo("about");

    // ==================== 关于页：连点版本号解锁调试（对照 CI AboutSettingsPage.DebugBorder） ====================

    /// <summary>启用调试菜单时必须手打一遍的免责声明（对照 CI 的验证文本）。</summary>
    private const string DebugUnlockPhrase = "我知晓我所做的行为开发者不对此负责";

    private int _versionClickCount;

    /// <summary>版本号连点 10 次 → 弹 Fluent 对话框、手打免责声明 → 解锁「调试」页（左栏底部）。</summary>
    private async void OnVersionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var s = AppServices.Settings;
        if (s.IsDebugOptionsEnabled)
            return;

        _versionClickCount++;
        if (_versionClickCount < 10)
            return;

        _versionClickCount = 0;

        // 对照 CI：必须先输入确认文本，输入正确才解锁
        if (!await ConfirmDebugUnlockAsync())
            return;

        s.IsDebugOptionsEnabled = true;
        AppServices.SaveSettings();
        RefreshDebugVisibility();
        NavigateTo("debug");
    }

    /// <summary>启用调试菜单的确认对话框（Fluent ContentDialog）：要求手打 <see cref="DebugUnlockPhrase"/>。</summary>
    private async Task<bool> ConfirmDebugUnlockAsync()
    {
        var input = new TextBox
        {
            Watermark = DebugUnlockPhrase,
            AcceptsReturn = false,
        };

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = "您正在启用仅供开发使用的调试菜单。此页面中的功能仅限开发与调试用途，"
                           + "开发者不对以非开发用途使用本页面功能造成的任何后果负责，"
                           + "也不接受因此产生的 Bug 反馈。",
                },
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = $"如果您确实要启用，请在下方文本框中输入：{DebugUnlockPhrase}",
                },
                input,
            },
        };

        var dialog = new ContentDialog
        {
            Title = "启用调试菜单",
            Content = content,
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
            return false;

        if (string.Equals(input.Text?.Trim(), DebugUnlockPhrase, StringComparison.Ordinal))
            return true;

        // 输入不对：提示后不放行（对照 CI 的 ShowWarningToast）
        await new ContentDialog
        {
            Title = "验证结果不正确",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = $"输入的文本与要求的不一致，请重新操作并输入：{DebugUnlockPhrase}",
            },
            CloseButtonText = "知道了",
        }.ShowAsync();

        return false;
    }

#if DEBUG
    /// <summary>调试：CLASSNEX_VERIFY_DIALOG=1 时启动后自动弹出「启用调试菜单」对话框，便于核对样式。</summary>
    private void VerifyDialogHook()
    {
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_DIALOG") != "1")
            return;

        DispatcherTimer.RunOnce(async () => await ConfirmDebugUnlockAsync(), TimeSpan.FromSeconds(2));
    }
#endif

    private void RefreshDebugVisibility() => DebugNavItem.IsVisible = AppServices.Settings.IsDebugOptionsEnabled;

    private void OnDebugClose()
    {
        AppServices.Settings.IsDebugOptionsEnabled = false;
        AppServices.SaveSettings();
        RefreshDebugVisibility();
        NavigateTo("about");
    }

    private void OnDebugTriggerClassOn() => AppServices.NotificationService?.DebugTriggerClassOn();

    private void OnDebugTriggerClassOff() => AppServices.NotificationService?.DebugTriggerClassOff();

    /// <summary>调试页三个条目的事件在代码里挂（AvaloniaFluentUI 的 SettingsExpanderItem.Click 在 XAML 里接方法名会编译失败）。</summary>
    private void WireDebugPageEvents()
    {
        DebugTriggerClassOnItem.Click += (_, _) => OnDebugTriggerClassOn();
        DebugTriggerClassOffItem.Click += (_, _) => OnDebugTriggerClassOff();
        DebugCloseItem.Click += (_, _) => OnDebugClose();
    }

    private async void OnOpenPrivacyPolicy(object? sender, RoutedEventArgs e)
    {
        await new ContentDialog
        {
            Title = "隐私政策",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "ClassNEX 是一款完全在本机运行的桌面课表悬浮窗。" +
                       "课表、设置与账户信息（头像 / 昵称 / 邮箱，仅用于界面展示）全部保存在你自己的电脑上，不会上传到任何服务器。" +
                       "应用联网的唯一用途是从 GitHub 获取开源代码与发布包。" +
                       "我们不会收集、出售或与第三方共享你的任何数据。"
            },
            PrimaryButtonText = "知道了",
            DefaultButton = ContentDialogButton.Primary
        }.ShowAsync();
    }

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

    /// <summary>组件库条目（ListBox 绑定用；点击选中会出现 Fluent 左侧强调色小竖线）。</summary>
    private sealed record WidgetTypeItem(string DisplayName, string Description, string Glyph, string Type);

    private void RefreshWidgetLibrary()
    {
        WidgetLibraryList.ItemsSource = AppServices.Widgets.AvailableTypes
            .Select(t => new WidgetTypeItem(t.DisplayName, t.Description, t.Glyph, t.Type))
            .ToList();
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

            // 重建该组件的**专属**设置项（每种组件一套，见 WidgetSettingsBuilder）
            WidgetSettingsPanel.Children.Clear();
            foreach (var row in WidgetSettingsBuilder.Build(config, OnWidgetSettingChanged))
                WidgetSettingsPanel.Children.Add(row);

            WidgetNoSettingsText.IsVisible = !WidgetSettingsBuilder.HasSettings(config.Type);
        }

        _loading = false;
    }

    /// <summary>「基本 → 行为」各项：载入 + 接线（开机自启/Url 协议会写注册表）。</summary>
    private void LoadBehaviorSettings()
    {
        var s = AppServices.Settings;

        StartupCheck.IsChecked = s.RunAtStartup;
        UrlProtocolCheck.IsChecked = s.RegisterUrlProtocol;
        WeekRotationBox.Value = Math.Clamp(s.WeekRotationOffset + 1, 1, 99);
        SafeModeCheck.IsChecked = s.TeachingSafeMode;
        SafeModeCombo.SelectedIndex = Math.Clamp(s.CrashHandlingMode, 0, 2);
        StartupSplashCheck.IsChecked = s.ShowStartupSplash;

        StartupCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.RunAtStartup = StartupCheck.IsChecked == true;
            AppServices.SaveSettings();
            StartupRegistry.SetRunAtStartup(AppServices.Settings.RunAtStartup);
        };

        UrlProtocolCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.RegisterUrlProtocol = UrlProtocolCheck.IsChecked == true;
            AppServices.SaveSettings();
            StartupRegistry.SetUrlProtocol(AppServices.Settings.RegisterUrlProtocol);
        };

        WeekRotationBox.ValueChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.WeekRotationOffset = (int)Math.Clamp(WeekRotationBox.Value, 1, 99) - 1;
            AppServices.SaveSettings();
        };

        SafeModeCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.TeachingSafeMode = SafeModeCheck.IsChecked == true;
            AppServices.SaveSettings();
        };

        SafeModeCombo.SelectionChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.CrashHandlingMode = Math.Clamp(SafeModeCombo.SelectedIndex, 0, 2);
            AppServices.SaveSettings();
        };

        StartupSplashCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.ShowStartupSplash = StartupSplashCheck.IsChecked == true;
            AppServices.SaveSettings();
        };
    }

    /// <summary>把设置里的提醒配置载入控件并挂事件（对照 CI ClassNotificationSettings 的字段与默认值）。</summary>
    private void LoadNotificationSettings()
    {
        var n = AppServices.Settings.Notification;
        PreparingEnabledCheck.IsChecked = n.IsClassOnPreparingNotificationEnabled;
        PreparingSpeechCheck.IsChecked = n.IsSpeechEnabledOnClassPreparing;
        ClassOnEnabledCheck.IsChecked = n.IsClassOnNotificationEnabled;
        ClassOnSpeechCheck.IsChecked = n.IsSpeechEnabledOnClassOn;
        ClassOffEnabledCheck.IsChecked = n.IsClassOffNotificationEnabled;
        ClassOffSpeechCheck.IsChecked = n.IsSpeechEnabledOnClassOff;
        ShowTeacherCheck.IsChecked = n.ShowTeacherName;
        IndoorPreparingBox.Value = Math.Clamp(n.InDoorClassPreparingDeltaTime, 0, 3600);
        OutdoorPreparingBox.Value = Math.Clamp(n.OutDoorClassPreparingDeltaTime, 0, 3600);
        PreparingText.Text = n.ClassOnPreparingText;
        PreparingMaskText.Text = n.ClassOnPreparingMaskText;
        OutdoorPreparingText.Text = n.OutdoorClassOnPreparingText;
        OutdoorPreparingMaskText.Text = n.OutdoorClassOnPreparingMaskText;
        ClassOnMaskBox.Text = n.ClassOnMaskText;
        ClassOffOverlayBox.Text = n.ClassOffOverlayText;

        PreparingEnabledCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        PreparingSpeechCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ClassOnEnabledCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ClassOnSpeechCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ClassOffEnabledCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ClassOffSpeechCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ShowTeacherCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        IndoorPreparingBox.ValueChanged += (_, _) => SaveNotificationSettings();
        OutdoorPreparingBox.ValueChanged += (_, _) => SaveNotificationSettings();
        PreparingText.TextChanged += (_, _) => SaveNotificationSettings();
        PreparingMaskText.TextChanged += (_, _) => SaveNotificationSettings();
        OutdoorPreparingText.TextChanged += (_, _) => SaveNotificationSettings();
        OutdoorPreparingMaskText.TextChanged += (_, _) => SaveNotificationSettings();
        ClassOnMaskBox.TextChanged += (_, _) => SaveNotificationSettings();
        ClassOffOverlayBox.TextChanged += (_, _) => SaveNotificationSettings();
    }

    /// <summary>把提醒设置控件写回设置并保存（值变化时调用）。</summary>
    private void SaveNotificationSettings()
    {
        if (_loading)
            return;

        var n = AppServices.Settings.Notification;
        n.IsClassOnPreparingNotificationEnabled = PreparingEnabledCheck.IsChecked == true;
        n.IsSpeechEnabledOnClassPreparing = PreparingSpeechCheck.IsChecked == true;
        n.IsClassOnNotificationEnabled = ClassOnEnabledCheck.IsChecked == true;
        n.IsSpeechEnabledOnClassOn = ClassOnSpeechCheck.IsChecked == true;
        n.IsClassOffNotificationEnabled = ClassOffEnabledCheck.IsChecked == true;
        n.IsSpeechEnabledOnClassOff = ClassOffSpeechCheck.IsChecked == true;
        n.ShowTeacherName = ShowTeacherCheck.IsChecked == true;
        n.InDoorClassPreparingDeltaTime = (int)Math.Clamp(IndoorPreparingBox.Value, 0, 3600);
        n.OutDoorClassPreparingDeltaTime = (int)Math.Clamp(OutdoorPreparingBox.Value, 0, 3600);
        n.ClassOnPreparingText = PreparingText.Text ?? "";
        n.ClassOnPreparingMaskText = PreparingMaskText.Text ?? "";
        n.OutdoorClassOnPreparingText = OutdoorPreparingText.Text ?? "";
        n.OutdoorClassOnPreparingMaskText = OutdoorPreparingMaskText.Text ?? "";
        n.ClassOnMaskText = ClassOnMaskBox.Text ?? "";
        n.ClassOffOverlayText = ClassOffOverlayBox.Text ?? "";

        AppServices.SaveSettings();
    }

    private void ApplyWidgetEdit()
    {
        if (_loading || _currentWidget is null)
            return;

        var config = _currentWidget.Config;
        config.IsEnabled = WidgetEnabledCheck.IsChecked == true;
        config.FontScale = WidgetFontSlider.Value;

        WidgetFontValueText.Text = $"{config.FontScale:0.00}x";
        _currentWidget.Refresh();

        AppServices.Widgets.Save();
    }

    /// <summary>
    /// 组件**专属**设置变化后的回调（值已由 <see cref="WidgetSettingsBuilder"/> 直接写进 config）：
    /// 刷新浮窗上该组件并落盘。
    /// </summary>
    private void OnWidgetSettingChanged()
    {
        if (_loading || _currentWidget is null)
            return;

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
    }
}
