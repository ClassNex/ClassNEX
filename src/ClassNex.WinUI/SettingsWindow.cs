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
        new("about", "关于", "\uE946", ""), new("appearance", "外观与主题", "\uE790", "主题、透明度与桌面课表缩放。", "widgets"),
        new("window", "窗口行为", "\uE737", "排列、置顶和鼠标穿透。", "widgets"), new("schedule", "课表与档案", "\uE787", "查看、编辑或导入课程档案。", "basic"),
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
    private readonly StackPanel _identityLabels = new() { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _identityName = Text("");
    private readonly TextBlock _identityEmail = Text("", "CaptionTextBlockStyle");
    private readonly PersonPicture _identityPhoto = new() { Width = 40, Height = 40 };
    private readonly Button _identityButton = new();
    private readonly Button _searchCompact = new();
    private Action? _refreshIdentity;
    private Expander? _currentExpander;
    private TextBlock? _pageHeading;
    private string? _requestedTarget;
    private long _navigationTicket;
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
        _titleBar.PaneToggleRequested += (_, _) => _navigation.IsPaneOpen = !_navigation.IsPaneOpen;
        _shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _shell.Children.Add(_titleBar);
        Grid.SetRow(_navigation, 1);
        _shell.Children.Add(_navigation);
        _navigation.IsSettingsVisible = false;
        _navigation.IsTitleBarAutoPaddingEnabled = false;
        _navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
        _navigation.IsPaneToggleButtonVisible = false;
        _navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
        _navigation.CompactModeThresholdWidth = 0;
        _navigation.ExpandedModeThresholdWidth = 840;
        _navigation.OpenPaneLength = 260;
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
            if (_page?.Content is ScrollViewer scroll && CurrentPageKey != "notification") _scrollPositions[CurrentPageKey] = scroll.VerticalOffset;
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
        bool? wide = null;
        void SetPane(double width)
        {
            var expanded = width >= _navigation.ExpandedModeThresholdWidth;
            if (expanded == wide) return;
            wide = expanded;
            _navigation.PaneDisplayMode = expanded ? NavigationViewPaneDisplayMode.Left : NavigationViewPaneDisplayMode.LeftCompact;
            _navigation.IsPaneOpen = expanded;
        }
        SetPane(AppWindow.Size.Width / (Math.Max(96, GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this))) / 96d));
        _shell.SizeChanged += (_, args) => { if (args.NewSize.Width > 0) SetPane(args.NewSize.Width); };
        RefreshSettings();
        Activated += (_, _) => { if (!_closed && _page is null) NavigateTo("home"); };
        Closed += (_, _) => { CommitInputs(); _closed = true; StopObservingSystemEffects(); SetHomeClock(false); LeavePageServices(); _bindings.Clear(); _targets.Clear(); _fieldCommits.Clear(); };
    }

    public void NavigateTo(string key) => Navigate(key, null);

    private static string ResolveKey(string key) => key.Trim().ToLowerInvariant() switch
    {
        "overview" or "总览" or "设置主页" or "settings" => "home", "general" or "system" or "startup" or "system-startup" or "系统与启动" or "基本" => "basic",
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
        if (CurrentPageKey == key) { FocusDestination(target); _requestedTarget = null; return; }
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
        _page = page;
        CurrentPageKey = key;
        _navigation.SelectedItem = _items[Destinations.Single(d => d.Key == key).Parent ?? key];
        if (_navigation.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftCompact) _navigation.IsPaneOpen = false;
        TrimBackStack();
        RenderPage();
        SetHomeClock(key == "home");
        var restore = args.NavigationMode is NavigationMode.Back or NavigationMode.Forward && _scrollPositions.TryGetValue(key, out var offset) ? offset : 0;
        FocusDestination(_requestedTarget, restore);
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
                if (element is Control control) control.Focus(FocusState.Programmatic);
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
        _body = new StackPanel { Spacing = 12, Padding = new Thickness(28, 18, 28, 36), MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Top };
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        var body = _body;
        bool? compact = null;
        scroll.SizeChanged += (_, size) =>
        {
            var next = size.NewSize.Width < 550;
            if (next == compact) return;
            compact = next;
            body.Padding = next ? new Thickness(16, 12, 16, 24) : new Thickness(28, 18, 28, 36);
        };
        var info = Destinations.Single(d => d.Key == key);
        Identify(scroll, "Page_" + key, info.Title);
        page.Content = scroll;
        _pageHeading = Text(info.Title, "TitleTextBlockStyle");
        _pageHeading.AllowFocusOnInteraction = false;
        _pageHeading.IsTabStop = true;
        Identify(_pageHeading, "SettingsPageTitle", info.Title);
        _body.Children.Add(_pageHeading);
        if (info.Description.Length != 0) _body.Children.Add(Text(info.Description));
        switch (key)
        {
            case "home": HomePage(); break;
            case "basic": BasicPage(); break;
            case "appearance": AppearancePage(); break;
            case "window": WindowPage(); break;
            case "widgets": WidgetsPage(); break;
            case "plugins": PluginsPage(); break;
            case "schedule": SchedulePage(); break;
            case "central": CentralPage(); break;
            case "privacy": PrivacyPage(); break;
            case "update": UpdatePage(); break;
            case "nex": NexPage(); break;
            case "notification": NotificationPage(); break;
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
        if (CurrentPageKey == "widgets" && _widgetShape != WidgetShape(settings))
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
        new("系统与启动", "basic", "RunAtStartup"), new("基本设置", "basic"), new("总览", "home"), new("开机自启", "basic", "RunAtStartup"), new("托盘图标", "basic", "TrayBehavior"), new("集控服务地址", "central", "CentralUrl"),
        new("周课表", "schedule", "ProfileWeek"), new("时间表与课间", "schedule", "ProfileTimes"), new("档案管理", "schedule", "ProfileInfo"), new("临时调课", "schedule", "ProfileSwap"),
        new("学期开始日期", "basic", "SemesterStart"), new("教学安全模式", "basic", "TeachingSafeMode"), new("主题（深色 / 浅色 / 系统）", "appearance", "Theme"),
        new("主界面背景不透明度", "appearance", "Opacity"), new("全局字号缩放", "appearance", "FontScale"), new("主界面缩放", "appearance", "MainScale"),
        new("窗口置顶", "window", "Topmost"), new("鼠标穿透", "window", "ClickThrough"), new("组件库", "widgets", "WidgetLibrary"),
        new("课前准备提醒", "notification", "ClassPrepare"), new("下课前剩余分钟", "notification", "PrepareMinutes"),
        new("上课提醒", "notification", "ClassOn"), new("下课提醒", "notification", "ClassOff"), new("科任老师", "notification", "Teacher"),
        new("默认权限", "nex", "NexPermission"), new("语音唤醒", "nex", "VoiceWake"), new("侧边唤醒条", "nex", "EdgeBar"),
        new("浏览器登录", "account", "AccountAction"), new("版本", "about", "Version"), new("拉起测试提醒", "debug", "NotifyTest")
    ];

    private IEnumerable<SearchHit> AllSearchHits() => Destinations.Select(d => new SearchHit(d.Title, d.Key)).Concat(SearchDetails)
        .Where(hit => hit.Key != "debug" || _bridge.ReadSettings().IsDebugOptionsEnabled);

    private IEnumerable<SearchHit> Search(string input)
    {
        var text = input.Trim();
        return text.Length == 0 ? [] : AllSearchHits().Where(hit => hit.Label.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(18);
    }
}
