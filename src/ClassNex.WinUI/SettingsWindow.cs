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
    private sealed record Destination(string Key, string Title, string Glyph, string Description);

    private static readonly Destination[] Destinations =
    [
        new("home", "主页", "\uE80F", "课堂与应用概览"), new("basic", "基本", "\uE713", "启动、学期、托盘和集控"),
        new("appearance", "外观", "\uE790", "主题、透明度与缩放"), new("window", "窗口", "\uE737", "桌面课表的窗口行为"),
        new("widgets", "组件", "\uECA5", "添加、排序和设置桌面组件"), new("nex", "NEX Intelligence", "\uE945", "助手、权限和唤醒方式"),
        new("notification", "提醒", "\uEA8F", "课前、上课与下课提醒"), new("account", "账户", "\uE77B", "ClassNEX ID 和浏览器授权"),
        new("about", "关于 ClassNEX", "\uE946", "版本、开源许可与反馈"), new("debug", "调试", "\uEBE8", "测试提醒和自定义助手接口"),
        new("settings", "设置", "\uE713", "浏览所有设置分类")
    ];

    private readonly DesktopBridge _bridge;
    private readonly Frame _frame = new();
    private readonly NavigationView _navigation = new();
    private readonly AutoSuggestBox _search = new() { PlaceholderText = "查找设置" };
    private readonly Grid _shell = new();
    private readonly Dictionary<string, NavigationViewItem> _items = [];
    private readonly List<Action<AppSettings>> _bindings = [];
    private readonly Dictionary<string, FrameworkElement> _targets = [];
    private readonly List<Action> _fieldCommits = [];
    private readonly UISettings _motion = new();
    private bool _refreshing, _closed;
    private int _versionClicks;
    private StackPanel _body = null!;
    private Page? _page;
    public string CurrentPageKey { get; private set; } = "";

    public NativeSettingsWindow(DesktopBridge bridge)
    {
        _bridge = bridge;
        Title = "ClassNEX · 应用设置";
        SystemBackdrop = new MicaBackdrop();
        ExtendsContentIntoTitleBar = true;
        var titleBar = new TitleBar { Title = "ClassNEX", Subtitle = "应用设置" };
        titleBar.RightHeader = CreateOptionsMenu();
        _shell.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _shell.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _shell.Children.Add(titleBar);
        Grid.SetRow(_navigation, 1);
        _shell.Children.Add(_navigation);
        _navigation.IsSettingsVisible = false;
        _navigation.IsTitleBarAutoPaddingEnabled = false;
        _navigation.IsBackButtonVisible = NavigationViewBackButtonVisible.Visible;
        _navigation.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
        _navigation.CompactModeThresholdWidth = 620;
        _navigation.ExpandedModeThresholdWidth = 840;
        _navigation.OpenPaneLength = 232;
        _navigation.AlwaysShowHeader = false;
        _navigation.AutoSuggestBox = _search;
        _navigation.Content = _frame;
        Identify(_navigation, "SettingsNavigation", "设置分类");
        Identify(_frame, "SettingsFrame", "设置内容");
        Identify(_search, "SettingsSearch", "查找设置");
        _frame.ContentTransitions = new TransitionCollection { new NavigationThemeTransition() };
        foreach (var destination in Destinations)
        {
            var item = new NavigationViewItem { Content = destination.Title, Tag = destination.Key, Icon = new FontIcon { Glyph = destination.Glyph } };
            Identify(item, "Nav_" + destination.Key, destination.Title);
            if (destination.Key != "settings") _navigation.MenuItems.Add(item);
            _items.Add(destination.Key, item);
        }
        _navigation.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer?.Tag is string key) Navigate(key, args.RecommendedNavigationTransitionInfo);
        };
        _navigation.BackRequested += (_, _) => Back();
        _frame.Navigating += (_, _) => CommitInputs();
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
            NavigateTo(hit.Key);
            if (hit.Target is not null && _targets.TryGetValue(hit.Target, out var target))
            {
                var destination = _page;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_closed || _page != destination) return;
                    target.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _motion.AnimationsEnabled });
                    if (target is Control control) control.Focus(FocusState.Programmatic);
                });
            }
        };
        var back = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        back.Invoked += (_, args) => { if (_frame.CanGoBack) { Back(); args.Handled = true; } };
        _shell.KeyboardAccelerators.Add(back);
        var find = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
        find.Invoked += (_, args) => { _navigation.IsPaneOpen = true; _search.Focus(FocusState.Keyboard); args.Handled = true; };
        _shell.KeyboardAccelerators.Add(find);
        Content = _shell;
        SetTitleBar(titleBar);
        ConfigureShellWindow();
        RefreshSettings();
        Activated += (_, _) => { if (!_closed && _page is null) NavigateTo("home"); };
        Closed += (_, _) => { CommitInputs(); _closed = true; SetHomeClock(false); _bindings.Clear(); _targets.Clear(); _fieldCommits.Clear(); };
    }

    public void NavigateTo(string key) => Navigate(key, null);

    private void Navigate(string key, NavigationTransitionInfo? recommended)
    {
        if (_closed) return;
        key = key switch { "general" => "basic", "interface" => "appearance", _ => key };
        if (!_items.ContainsKey(key) || key == "debug" && !_bridge.ReadSettings().IsDebugOptionsEnabled) key = "home";
        if (CurrentPageKey == key) return;
        NavigationTransitionInfo transition = _page is null || !_motion.AnimationsEnabled ? new SuppressNavigationTransitionInfo()
            : recommended ?? new EntranceNavigationTransitionInfo();
        _frame.Navigate(typeof(Page), key, transition);
    }

    private void Back()
    {
        TrimBackStack();
        if (!_frame.CanGoBack) return;
        if (_motion.AnimationsEnabled) _frame.GoBack();
        else _frame.GoBack(new SuppressNavigationTransitionInfo());
    }

    private void TrimBackStack()
    {
        if (!_bridge.ReadSettings().IsDebugOptionsEnabled)
            for (var i = _frame.BackStack.Count - 1; i >= 0; i--)
                if (Equals(_frame.BackStack[i].Parameter, "debug")) _frame.BackStack.RemoveAt(i);
        _navigation.IsBackEnabled = _frame.CanGoBack;
    }

    private void OnNavigated(object sender, NavigationEventArgs args)
    {
        if (args.Parameter is not string key || args.Content is not Page page) return;
        _page = page;
        CurrentPageKey = key;
        _navigation.SelectedItem = key == "settings" ? null : _items[key];
        TrimBackStack();
        RenderPage();
        SetHomeClock(key == "home");
    }

    private void RenderPage()
    {
        if (_page is null) return;
        _bindings.Clear();
        _targets.Clear();
        _fieldCommits.Clear();
        var page = _page;
        var key = CurrentPageKey;
        _body = new StackPanel { Spacing = 12, Padding = new Thickness(28, 18, 28, 36), MaxWidth = 1080, HorizontalAlignment = HorizontalAlignment.Stretch };
        var scroll = new ScrollViewer { Content = _body, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var body = _body;
        bool? compact = null;
        scroll.SizeChanged += (_, size) =>
        {
            var next = size.NewSize.Width < 550;
            if (next == compact) return;
            compact = next;
            body.Padding = next ? new Thickness(16, 12, 16, 24) : new Thickness(28, 18, 28, 36);
        };
        Identify(scroll, "Page_" + key, _items[key].Content.ToString() ?? key);
        page.Content = scroll;
        var info = Destinations.Single(d => d.Key == key);
        if (key != "home")
        {
            _body.Children.Add(Text(info.Title, "TitleTextBlockStyle"));
            _body.Children.Add(Text(info.Description, "BodyTextBlockStyle"));
        }
        switch (key)
        {
            case "home": HomePage(); break;
            case "settings": OverviewPage(); break;
            case "basic": BasicPage(); break;
            case "appearance": AppearancePage(); break;
            case "window": WindowPage(); break;
            case "widgets": WidgetsPage(); break;
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
            _items["debug"].Visibility = settings.IsDebugOptionsEnabled ? Visibility.Visible : Visibility.Collapsed;
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
        new("开机自启", "basic", "RunAtStartup"), new("托盘图标", "basic", "TrayBehavior"), new("集控服务地址", "basic", "CentralUrl"),
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
