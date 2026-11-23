using ClassNex.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow : Window
{
    private sealed record Destination(string Key, string Title, string Glyph, string Description, string? Parent = null);

    private static readonly Destination[] Destinations =
    [
        new("home", "主页", "\uE80F", ""), new("basic", "通用", "\uE713", "启动方式、课堂偏好与本地档案。"),
        new("widgets", "组件与主界面", "\uECA5", "调整桌面课表的内容、顺序和窗口行为。"),
        new("notification", "通知服务", "\uEA8F", "管理课堂提醒的时机和内容。"), new("account", "账户", "\uE77B", "ClassNEX ID 与浏览器登录。"),
        new("plugins", "插件市场", "\uE74C", "管理安装在这台电脑上的插件。"), new("central", "集控与自动化", "\uE968", "连接设备，查看课表与设置的同步状态。"),
        new("nex", "NEX Intelligence与NEX AI", "\uE945", "配置课堂助手、权限和唤醒方式。"),
        new("privacy", "隐私与安全性", "\uE72E", "查看本地数据和联网功能的使用方式。"), new("update", "更新", "\uE895", "查看已安装版本及公开发布记录。"),
        new("about", "关于", "\uE946", ""),
        new("startup", "启动与托盘", "\uE7E8", "开机自启、启动画面、URL 协议和托盘菜单。", "basic"),
        new("schedule", "课表与档案", "\uE787", "单周起始日、今日课表和本机课程档案。", "basic"),
        new("runtime", "运行与恢复", "\uE9D9", "教学安全模式和发生异常时的处理方式。", "basic"),
        new("widget-layout", "组件布局", "\uECA5", "编辑桌面布局，添加、移除和排列组件。", "widgets"),
        new("appearance", "外观与主题", "\uE790", "主题、透明度、字号和桌面课表缩放。", "widgets"),
        new("window", "窗口行为", "\uE737", "排列方向、窗口置顶、鼠标穿透和移入淡化。", "widgets"),
        new("reminder-rules", "上下课提醒", "\uE823", "课前准备、上课和课间休息。", "notification"),
        new("reminder-after-school", "放学提醒", "\uE7EE", "当天最后一节课结束时显示放学提示。", "notification"),
        new("reminder-display", "提醒方式", "\uE9E9", "视觉强调、提醒铃声和课程信息。", "notification"),
        new("nex-assistant", "课堂助手", "\uE8BD", "打开对话、设置默认权限和自定义提示词。", "nex"),
        new("nex-wake", "唤醒方式", "\uE720", "语音唤醒、侧边唤醒条和麦克风诊断。", "nex"),
        new("privacy-storage", "本机数据", "\uE8B7", "打开课表、应用设置和运行日志所在目录。", "privacy"),
        new("privacy-services", "联网与权限", "\uE774", "管理账户、集控、语音和插件使用的服务。", "privacy"),
        new("debug", "开发者选项", "\uEBE8", "测试课堂提醒和自定义助手接口。", "about")
    ];

    private readonly DesktopBridge _bridge;
    private readonly Frame _frame = new();
    private readonly NavigationView _navigation = new();
    private readonly AutoSuggestBox _search = new() { PlaceholderText = "查找设置" };
    private readonly Grid _shell = new();
    private readonly TitleBar _titleBar = new() { Title = "ClassNEX", Subtitle = "应用设置", IsBackButtonVisible = true, IsPaneToggleButtonVisible = true };
    private readonly Dictionary<string, NavigationViewItem> _items = [];
    private readonly List<Action<AppSettings>> _bindings = [];
    private readonly Dictionary<string, FrameworkElement> _targets = [];
    private readonly Dictionary<string, Expander> _detailOwners = [];
    private readonly List<Action> _fieldCommits = [];
    private readonly Dictionary<string, double> _scrollPositions = [];
    private readonly UISettings _motion = new();
    private readonly StackPanel _identityLabels = new() { Spacing = 2, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _identityName = Text("");
    private readonly TextBlock _identityEmail = Text("", "CaptionTextBlockStyle");
    private readonly PersonPicture _identityPhoto = new() { Width = 30, Height = 30 };
    private readonly Button _identityButton = new();
    private readonly Button _searchCompact = new();
    private Action? _refreshIdentity;
    private Expander? _currentExpander;
    private UIElement? _pageHeading;
    private string? _requestedTarget;
    private long _navigationTicket;
    private bool _widePaneOpen = true;
    private bool _refreshing, _closed;
    private int _versionClicks;
    private StackPanel _body = null!;
    private Page? _page;
    public string CurrentPageKey { get; private set; } = "";

    public NativeSettingsWindow(DesktopBridge bridge)
    {
        _bridge = bridge;
        Title = "ClassNEX · 应用设置";
        ExtendsContentIntoTitleBar = true;
        _titleBar.RightHeader = CreateOptionsMenu();
        _titleBar.BackRequested += (_, _) => Back();
        _titleBar.PaneToggleRequested += (_, _) => SetPaneFromUser(!_navigation.IsPaneOpen);
        _shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _shell.Children.Add(_titleBar);
        Grid.SetRow(_navigation, 1);
        _shell.Children.Add(_navigation);
        _navigation.IsSettingsVisible = false;
        _navigation.IsTitleBarAutoPaddingEnabled = false;
        _navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        _navigation.IsPaneToggleButtonVisible = false;
        _navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
        _navigation.CompactModeThresholdWidth = 0;
        _navigation.ExpandedModeThresholdWidth = 840;
        _navigation.OpenPaneLength = 260;
        _navigation.DisplayModeChanged += (_, args) =>
        {
            if (args.DisplayMode != NavigationViewDisplayMode.Expanded) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed && _navigation.DisplayMode == NavigationViewDisplayMode.Expanded && _navigation.IsPaneOpen != _widePaneOpen)
                    _navigation.IsPaneOpen = _widePaneOpen;
            });
        };
        _navigation.AlwaysShowHeader = false;
        BuildPane();
        ConfigureSurfaceResources();
        _navigation.Content = _frame;
        Identify(_navigation, "SettingsNavigation", "设置分类");
        Identify(_frame, "SettingsFrame", "设置内容");
        Identify(_search, "SettingsSearch", "查找设置");
        _frame.ContentTransitions = new TransitionCollection { new NavigationThemeTransition() };
        foreach (var destination in Destinations)
        {
            if (destination.Parent != null) continue;
            var item = new NavigationViewItem { Content = destination.Title, Tag = destination.Key, Icon = new FontIcon { Glyph = destination.Glyph } };
            Identify(item, "Nav_" + destination.Key, destination.Title);
            ToolTipService.SetToolTip(item, destination.Title);
            _navigation.MenuItems.Add(item);
            _items.Add(destination.Key, item);
        }
        _navigation.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer?.Tag is string key) NavigateTo(key);
        };
        _navigation.BackRequested += (_, _) => Back();
        _frame.Navigating += (_, _) =>
        {
            CommitInputs();
            if (_page?.Content is ScrollViewer scroll && CurrentPageKey is not ("reminder-rules" or "reminder-display")) _scrollPositions[CurrentPageKey] = scroll.VerticalOffset;
        };
        _frame.Navigated += OnNavigated;
        _search.TextChanged += (_, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
                _search.ItemsSource = Search(_search.Text).Select(x => x.Label).ToArray();
        };
        _search.QuerySubmitted += (_, args) =>
        {
            var hit = args.ChosenSuggestion is string label ? AllSearchHits().FirstOrDefault(x => x.Label == label) : Search(args.QueryText).FirstOrDefault();
            if (hit is null) return;
            _search.Text = "";
            _search.ItemsSource = Array.Empty<string>();
            Navigate(hit.Key, hit.Target);
        };
        var back = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        back.Invoked += (_, args) => { if (_frame.CanGoBack) { Back(); args.Handled = true; } };
        _shell.KeyboardAccelerators.Add(back);
        var find = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        find.Invoked += (_, args) => { OpenSearch(); args.Handled = true; };
        _shell.KeyboardAccelerators.Add(find);
        Content = _shell;
        SetTitleBar(_titleBar);
        ConfigureShellWindow();
        ObserveSystemEffects();
        RefreshSettings();
        Activated += (_, _) => { if (!_closed && _page is null) NavigateTo("home"); };
        Closed += (_, _) => { CommitInputs(); _closed = true; StopObservingSystemEffects(); SetHomeClock(false); LeavePageServices(); _bindings.Clear(); _targets.Clear(); _fieldCommits.Clear(); };
    }

    public void NavigateTo(string key) => Navigate(key, null);

    private void SetPaneFromUser(bool open)
    {
        if (_navigation.DisplayMode == NavigationViewDisplayMode.Expanded) _widePaneOpen = open;
        _navigation.IsPaneOpen = open;
    }

    private static string ResolveKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "overview" or "总览" or "设置主页" or "settings" => "home", "general" or "system" or "基本" => "basic",
        "system-startup" or "系统与启动" => "startup", "widget-settings" or "编辑主界面" => "widget-layout",
        "interface" => "appearance", "theme" => "appearance", "components" => "widgets", "plugin" => "plugins", "notifications" or "reminders" or "提醒通知" => "notification",
        "ai" => "nex", "management" => "central", "profiles" or "profile" or "timetable" or "档案管理" or "课表与课程" => "schedule", "developer" or "调试" => "debug", "updates" => "update",
        var value => Destinations.FirstOrDefault(d => d.Key == value || d.Title.Equals(value, StringComparison.OrdinalIgnoreCase))?.Key ?? "home"
    };

    private void Navigate(string key, string? target)
    {
        if (_closed) return;
        key = ResolveKey(key);
        if (key == "debug" && !_bridge.ReadSettings().IsDebugOptionsEnabled) key = "about";
        _requestedTarget = target;
        if (CurrentPageKey == key)
        {
            if (_navigation.DisplayMode == NavigationViewDisplayMode.Compact) _navigation.IsPaneOpen = false;
            FocusDestination(target); _requestedTarget = null; return;
        }
        NavigationTransitionInfo transition = _page is null || !AnimationsEnabled ? new SuppressNavigationTransitionInfo()
            : Destinations.First(d => d.Key == key).Parent != null ? new DrillInNavigationTransitionInfo() : new EntranceNavigationTransitionInfo();
        _frame.Navigate(typeof(Page), key, transition);
    }

    private void Back()
    {
        _requestedTarget = null;
        TrimBackStack();
        if (!_frame.CanGoBack) return;
        if (AnimationsEnabled) _frame.GoBack();
        else _frame.GoBack(new SuppressNavigationTransitionInfo());
    }

    private void TrimBackStack()
    {
        if (!_bridge.ReadSettings().IsDebugOptionsEnabled)
            for (var i = _frame.BackStack.Count - 1; i >= 0; i--)
                if (Equals(_frame.BackStack[i].Parameter, "debug")) _frame.BackStack.RemoveAt(i);
        _navigation.IsBackEnabled = _frame.CanGoBack;
        _titleBar.IsBackButtonEnabled = _frame.CanGoBack;
    }

    private void OnNavigated(object sender, NavigationEventArgs args)
    {
        if (args.Parameter is not string key || args.Content is not Page page) return;
        var previous = CurrentPageKey;
        _page = page;
        CurrentPageKey = key;
        _navigation.SelectedItem = _items[Destinations.Single(d => d.Key == key).Parent ?? key];
        if (_navigation.DisplayMode == NavigationViewDisplayMode.Compact) _navigation.IsPaneOpen = false;
        TrimBackStack();
        RenderPage();
        SetHomeClock(key == "home");
        var restore = args.NavigationMode is NavigationMode.Back or NavigationMode.Forward && _scrollPositions.TryGetValue(key, out var offset) ? offset : 0;
        var target = _requestedTarget;
        if (target == null && args.NavigationMode == NavigationMode.Back && _targets.ContainsKey("Category_" + previous)) target = "Category_" + previous;
        FocusDestination(target, restore);
        _requestedTarget = null;
    }

    private void FocusDestination(string? target, double restore = 0)
    {
        var ticket = ++_navigationTicket;
        if (target != null && _detailOwners.TryGetValue(target, out var detail)) detail.IsExpanded = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_closed || ticket != _navigationTicket) return;
            if (target != null && _targets.TryGetValue(target, out var element))
            {
                element.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
                if (element.IsTabStop) element.Focus(FocusState.Programmatic);
            }
            else if (restore > 0 && _page?.Content is ScrollViewer scroll)
            {
                scroll.Focus(FocusState.Programmatic);
                scroll.ChangeView(null, restore, null, true);
            }
            else _pageHeading?.Focus(FocusState.Programmatic);
        });
    }

    private void RenderPage()
    {
        if (_page is null) return;
        _bindings.Clear();
        _targets.Clear();
        _detailOwners.Clear();
        LeavePageServices();
        _fieldCommits.Clear();
        var page = _page;
        var key = CurrentPageKey;
        _body = new StackPanel { Spacing = key == "home" ? 12 : 4, Padding = new Thickness(28, 18, 28, 36), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Top };
        scroll.RegisterPropertyChangedCallback(ScrollViewer.ViewportWidthProperty, static (sender, _) =>
        {
            var viewport = (ScrollViewer)sender;
            var width = viewport.ViewportWidth;
            if (width <= 0 || viewport.Content is not StackPanel body || body.Width == width) return;
            var compact = width < 550;
            var inset = compact ? 16 : 28 + Math.Max(0, (width - 1080) / 2);
            body.Width = width;
            body.Padding = new Thickness(inset, compact ? 12 : 18, inset, compact ? 24 : 36);
        });
        var info = Destinations.Single(d => d.Key == key);
        Identify(scroll, "Page_" + key, info.Title);
        page.Content = scroll;
        AddPageHeading(info);
        switch (key)
        {
            case "home": HomePage(); break;
            case "basic": case "widgets": case "nex": case "privacy": CategoryList(info); break;
            case "notification": NotificationOverview(info); break;
            case "startup": StartupPage(); break;
            case "runtime": RuntimePage(); break;
            case "appearance": AppearancePage(); break;
            case "window": WindowPage(); break;
            case "widget-layout": WidgetLayoutPage(); break;
            case "plugins": PluginsPage(); break;
            case "schedule": SchedulePage(); break;
            case "central": CentralPage(); break;
            case "privacy-storage": StoragePage(); break;
            case "privacy-services": PermissionsPage(); break;
            case "update": UpdatePage(); break;
            case "nex-assistant": AssistantPage(); break;
            case "nex-wake": WakePage(); break;
            case "reminder-rules": ReminderRulesPage(); break;
            case "reminder-after-school": AfterSchoolPage(); break;
            case "reminder-display": ReminderDisplayPage(); break;
            case "account": AccountPage(); break;
            case "about": AboutPage(); break;
            case "debug": DebugPage(); break;
        }
        RefreshSettings();
    }

    public void RefreshSettings()
    {
        if (_closed || _refreshing) return;
        var settings = _bridge.ReadSettings();
        if (CurrentPageKey == "widget-layout" && _widgetShape != WidgetShape(settings))
        {
            _pendingSlider = null;
            _sliderTimer?.Stop();
            RenderPage();
            return;
        }
        _refreshing = true;
        try
        {
            var theme = settings.ThemeMode switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default };
            if (_shell.RequestedTheme != theme) _shell.RequestedTheme = theme;
            _refreshIdentity?.Invoke();
            foreach (var apply in _bindings) apply(settings);
        }
        finally { _refreshing = false; }
        if (CurrentPageKey == "debug" && !settings.IsDebugOptionsEnabled) NavigateTo("about");
    }

    private void Edit(Action<AppSettings> update)
    {
        if (_closed || _refreshing) return;
        update(_bridge.ReadSettings());
        _bridge.SaveSettings();
        RefreshSettings();
    }

    private static void Identify(DependencyObject element, string id, string name)
    {
        AutomationProperties.SetAutomationId(element, id);
        AutomationProperties.SetName(element, name);
    }

    private sealed record SearchHit(string Title, string Key, string? Target = null)
    {
        public string Label => Target is null ? Title : $"{Title} · {Destinations.Single(d => d.Key == Key).Title}";
    }

    private static readonly SearchHit[] SearchDetails =
    [
        new("系统与启动", "startup", "RunAtStartup"), new("基本设置", "basic"), new("总览", "home"), new("开机自启", "startup", "RunAtStartup"), new("显示启动画面", "startup", "StartupSplash"), new("注册 URL 协议", "startup", "UrlProtocol"), new("托盘图标", "startup", "TrayMenu"), new("集控服务地址", "central", "CentralUrl"),
        new("周课表", "schedule", "ProfileWeek"), new("时间表与课间", "schedule", "ProfileTimes"), new("档案管理", "schedule", "ProfileInfo"), new("临时调课", "schedule", "ProfileSwap"),
        new("学期开始日期", "schedule", "SemesterStart"), new("单周起始日期", "schedule", "SemesterStart"), new("教学安全模式", "runtime", "TeachingSafeMode"), new("异常处理", "runtime", "CrashHandling"), new("主题（深色 / 浅色 / 系统）", "appearance", "Theme"),
        new("主界面背景不透明度", "appearance", "Opacity"), new("全局字号缩放", "appearance", "FontScale"), new("主界面缩放", "appearance", "MainScale"),
        new("窗口置顶", "window", "Topmost"), new("鼠标穿透", "window", "ClickThrough"), new("组件库", "widget-layout", "WidgetLibrary"),
        new("允许课堂提醒", "notification", "Notifications"), new("课前准备提醒", "reminder-rules", "ClassPrepare"), new("课间剩余分钟", "reminder-rules", "PrepareMinutes"),
        new("室外课程提前时间", "reminder-rules", "OutdoorSeconds"), new("上课提醒", "reminder-rules", "ClassOn"), new("下课提醒", "reminder-rules", "ClassOff"),
        new("放学提示文字", "reminder-after-school", "AfterSchoolText"), new("启用放学提醒", "reminder-after-school", "AfterSchoolEnabled"), new("预览课堂提醒", "notification", "PreviewNotification"),
        new("科任老师", "reminder-display", "Teacher"), new("次要提醒", "reminder-display", "MinorNotifications"), new("视觉强调", "reminder-display", "NotificationEffects"),
        new("提醒铃声", "reminder-display", "ReminderSound"), new("铃声音量", "reminder-display", "ReminderVolume"), new("自定义铃声", "reminder-display", "ReminderSoundFile"),
        new("默认权限", "nex-assistant", "NexPermission"), new("语音唤醒", "nex-wake", "VoiceWake"), new("侧边唤醒条", "nex-wake", "EdgeBar"),
        new("运行日志", "privacy-storage", "PrivacyLogs"), new("浏览器登录", "account", "AccountAction"), new("版本", "about", "Version"), new("拉起测试提醒", "debug", "NotifyTest")
    ];

    private IEnumerable<SearchHit> AllSearchHits() => Destinations.Select(d => new SearchHit(d.Title, d.Key)).Concat(SearchDetails)
        .Where(hit => hit.Key != "debug" || _bridge.ReadSettings().IsDebugOptionsEnabled);

    private IEnumerable<SearchHit> Search(string input)
    {
        var text = input.Trim();
        return text.Length == 0 ? [] : AllSearchHits().Where(hit => hit.Label.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(18);
    }
}
