using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.ViewModels;
using AvaloniaFluentUI.Controls;
using AvaloniaFluentUI.Windowing;

namespace ClassNex.Views;

public partial class SettingsWindow : AppWindow
{
    private readonly ObservableCollection<WidgetItem> _widgets = new();
    private readonly ObservableCollection<SearchEntry> _searchEntries = new();

    private readonly List<int> _pageHistory = new();
    private int _lastPageIndex = -1;
    private Control? _currentPageView;

    private bool _loading;
    private WidgetItem? _currentWidget;
    private int _lastWidgetTabIndex;

    private bool _closed;
    private bool _suppressHistory;
    private bool _settingEventsWired;
    private bool _notificationEventsWired;
    private bool _pluginsBusy;
    private bool _widgetSettingsDirty = true;
    private readonly DispatcherTimer _editSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private AppSettings? _pendingEdit;
    private readonly CancellationTokenSource _windowLifetime = new();

    internal string CurrentPageKey => _lastPageIndex < 0 ? "home" : Sections[_lastPageIndex].Key;

    private sealed record Section(string Key, string Title, string Description);

    private static readonly Section[] Sections =
    [
        new("home", "总览", ""),
        new("basic", "系统与启动", "配置开机启动、学期周期、托盘与故障处理。"),
        new("appearance", "外观与主题", "选择应用主题，调整课堂浮窗的字号、大小与透明度。"),
        new("window", "悬浮窗口", "调整桌面浮窗的排列方式与鼠标交互。"),
        new("widgets", "桌面组件", "选择要显示的内容，调整组件顺序或单独配置。"),
        new("nex", "NEX Intelligence", "管理对话、语音唤醒与模型连接。"),
        new("notification", "通知提醒", "分别设置课前、上课和下课时的提醒。"),
        new("about", "关于 ClassNEX", "版本、开放源代码许可与问题反馈。"),
        new("debug", "开发者选项", "手动触发提醒，检查课堂显示效果。"),
        new("account", "账户与登录", "通过浏览器登录，管理这台设备的 ClassNEX ID。"),
        new("schedule", "课表与课程", "查看今天的安排，编辑每周课程、时间段与科目。"),
        new("profile", "档案管理", "当前课表档案、文件位置与导入。"),
        new("central", "集控管理", "用同一账户连接集控，在管理端为设备分配班级。"),
        new("plugins", "插件与扩展", "管理本机安装的扩展。只安装来自可信开发者的插件。"),
    ];

    private sealed class SearchEntry
    {
        public string Title { get; init; } = "";

        public string PageName { get; init; } = "";

        public int PageIndex { get; init; }
    }

    private static readonly (string Title, int PageIndex)[] SearchIndex =
    {
        ("开机自启", 1),
        ("注册 Url 协议", 1),
        ("学期开始时间", 1),
        ("点击托盘图标行为", 1),
        ("教学安全模式", 1),
        ("主题（浅色 / 深色 / 跟随系统）", 2),
        ("主界面背景不透明度", 2),
        ("全局字号缩放", 2),
        ("主界面缩放", 2),
        ("组件排列方向", 3),
        ("鼠标穿透", 3),
        ("组件库（添加组件）", 4),
        ("恢复默认布局", 4),
        ("组件列表（上移 / 下移 / 删除）", 4),
        ("组件设置：启用该组件", 4),
        ("组件设置：字号缩放", 4),
        ("时钟组件显示秒", 4),
        ("自定义文本占位符", 4),
        ("NEX Intelligence 接口", 5),
        ("语音唤醒", 5),
        ("提醒", 6),
        ("课前提醒", 6),
        ("版本信息", 7),
        ("账户与浏览器授权", 9),
        ("编辑课程、时间表与科目", 10),
        ("档案管理与导入课表", 11),
        ("集控服务地址与连接状态", 12),
        ("启用或停用插件", 13),
    };

    public SettingsWindow()
    {
        InitializeComponent();
        WidgetList.ItemsSource = _widgets;
        _editSaveTimer.Tick += (_, _) => FlushContinuousInput();
        InitShell();
        WireEvents();
        LoadFromSettings();
        NavigateTo("home");
        AccountService.Changed += AccountChanged;
        AppServices.Schedule.ProfileChanged += ScheduleChanged;
        AppServices.ManagedSettingsApplied += ManagedSettingsChanged;
        if (AppServices.Management is { } management) management.StatusChanged += OnCentralManagementStatusChanged;
        if (AppServices.Plugins is { } plugins) plugins.Changed += PluginsChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            FlushContinuousInput();
            _editSaveTimer.Stop();
            _windowLifetime.Cancel();
            _windowLifetime.Dispose();
            _homeGreetingTimer?.Stop();
            if (_nexGlowPreviewTimer is not null)
            {
                _nexGlowPreviewTimer.Stop();
                AppServices.NexVoice?.GlowWindow?.StopGlow();
                AppServices.MainWindow?.HideNexPanel();
            }
            AccountService.Changed -= AccountChanged;
            AppServices.Schedule.ProfileChanged -= ScheduleChanged;
            AppServices.ManagedSettingsApplied -= ManagedSettingsChanged;
            if (AppServices.Management is { } service) service.StatusChanged -= OnCentralManagementStatusChanged;
            if (AppServices.Plugins is { } extensions) extensions.Changed -= PluginsChanged;
            if (AppServices.NexVoice is { } voice) voice.StatusChanged -= VoiceStatusChanged;
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && SearchResultsPanel.IsVisible)
            {
                SearchBox.Text = "";
                args.Handled = true;
            }
        };
    }

    private void DispatchWhileOpen(Action action)
    {
        if (_closed) return;
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(() => { if (!_closed) action(); });
    }

    private void AccountChanged() => DispatchWhileOpen(() =>
    {
        RefreshAccountName();
        RefreshCentralAccountUi();
        if (CurrentPageKey == "home") RefreshHomeGreeting();
    });

    private void ScheduleChanged() => DispatchWhileOpen(() =>
    {
        if (CurrentPageKey is "home" or "schedule" or "profile") RefreshClassroom();
    });

    private void ManagedSettingsChanged() => DispatchWhileOpen(() =>
    {
        LoadFromSettings();
        if (CurrentPageKey == "nex") { RefreshNexPage(); RefreshNexApiFields(); }
        if (CurrentPageKey is "home" or "schedule" or "profile") RefreshClassroom();
    });

    private void PluginsChanged() => DispatchWhileOpen(() =>
    {
        if (CurrentPageKey == "plugins") RefreshPlugins();
    });

    private void VoiceStatusChanged(string status) => DispatchWhileOpen(() => NexVoiceStatusText.Text = status);

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            if (TitleBar is { } bar)
                bar.TitleBarHitTestType = TitleBarHitTestType.Complex;

            AppWindow.SetAllowInteractionInTitleBar(this, true);
        }
        catch
        {

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
                WindowTransparencyLevel.None,
            };
            Background = Brushes.Transparent;
        }
        catch
        {

        }
    }

    private void InitShell()
    {
        var version = typeof(SettingsWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = typeof(SettingsWindow).Assembly.GetName().Version?.ToString() ?? "26w41a";

        VersionText.Text = version;
        AboutVersionText.Text = $"版本 {version}";

        RefreshAccountName();

        SearchResults.ItemsSource = _searchEntries;

#if DEBUG

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

    private void RefreshAccountName()
    {
        var profile = AccountService.Current;
        var loggedIn = profile is not null;
        var displayName = loggedIn ? profile!.DisplayName.Trim() : "尚未登录";
        var displayEmail = loggedIn ? profile!.Email.Trim() : "登录后自动同步账户资料";
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = loggedIn ? "ClassNEX 用户" : "尚未登录";
        if (string.IsNullOrWhiteSpace(displayEmail))
            displayEmail = loggedIn ? "未提供邮箱" : "登录后自动同步账户资料";

        AccountPageNameText.Text = displayName;
        AccountPageEmailText.Text = loggedIn ? displayEmail : "使用 ClassNEX ID 同步资料与连接集控。";
        AccountPageStatusText.Text = loggedIn ? "已登录" : "ClassNEX ID";
        AccountLoginButton.Content = loggedIn ? "切换账户" : "浏览器登录";
        AccountLogoutButton.IsVisible = loggedIn;
        AccountAvatar.Apply(AccountPageAvatarRing, profile?.DisplayName ?? "", AccountService.AvatarImagePath);
    }

    private void OnAccountLogin(object? sender, RoutedEventArgs e)
    {
        App.OpenLoginWindow();
    }

    private void OnAccountLogout(object? sender, RoutedEventArgs e)
    {
        AccountService.Logout();
        RefreshAccountName();
    }

    private void OnNavigateBack(object? sender, RoutedEventArgs e)
    {
        if (_pageHistory.Count == 0) return;
        var index = _pageHistory[^1];
        _pageHistory.RemoveAt(_pageHistory.Count - 1);
        _suppressHistory = true;
        try { NavigateTo(Sections[index].Key); }
        finally { _suppressHistory = false; }
        BackButton.IsEnabled = _pageHistory.Count > 0;
    }

    public void NavigateTo(string page)
    {
        var key = page switch { "overview" or "settings" => "home", "general" => "basic", "interface" => "appearance", _ => page };
        var item = NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(x => Equals(x.Tag, key) && x.IsVisible);
        if (item is null) return;
        if (ReferenceEquals(NavView.SelectedItem, item)) SwitchPage();
        else NavView.SelectedItem = item;
    }

    private void OnPageLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) NavigateTo(page);
    }

    private void OnEditorLink(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab } && int.TryParse(tab, out var index)) App.OpenProfileEditor(index);
    }

    private void WireEvents()
    {

        SearchBox.TextChanged += (_, _) => UpdateSearchResults();
        SearchResults.SelectionChanged += (_, _) => NavigateFromSearch();

        NavView.SelectionChanged += (_, _) => SwitchPage();
        NavView.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer is NavigationViewItem { Tag: string key } && key == CurrentPageKey) PageScroll.Offset = Vector.Zero;
        };
        SearchBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && _searchEntries.Count > 0)
            {
                SearchResults.SelectedItem = _searchEntries[0];
                args.Handled = true;
            }
        };

        SingleWeekStartPicker.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "SelectedDate")
                ApplyGeneral();
        };
        TrayBehaviorCombo.SelectionChanged += (_, _) => ApplyGeneral();

        ThemeCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
                ApplyTheme();
        };

        OpacitySlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface(true);
        };
        FontScaleSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface(true);
        };
        MainWindowScaleSlider.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "Value")
                ApplyInterface(true);
        };
        OrientationCombo.SelectionChanged += (_, _) => ApplyInterface();
        ClickThroughSwitch.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "IsChecked")
                ApplyInterface();
        };

        ResetWidgetButton.Click += (_, _) => ResetWidgets();
        WidgetList.SelectionChanged += (_, _) => SelectWidget();
        _lastWidgetTabIndex = WidgetTabs.SelectedIndex;
        WidgetTabs.SelectionChanged += (_, _) =>
        {
            var index = WidgetTabs.SelectedIndex;
            if (index < 0 || index == _lastWidgetTabIndex)
                return;

            var page = index == 0 ? (Control)WidgetLibraryPage : WidgetEditor;
            _lastWidgetTabIndex = index;
            WinUIPageTransitions.FadeIn(page);
        };

        CentralManagementToggle.IsCheckedChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.CentralManagement.IsEnabled =
                CentralManagementToggle.IsChecked == true;
            AppServices.SaveSettings();
        };
        CentralServerBox.TextChanged += (_, _) => SaveCentralManagementFields();
        CentralAccountLoginButton.Click += (_, _) => App.OpenLoginWindow();
        CentralConnectButton.Click += async (_, _) => await ConnectCentralManagementAsync();
        CentralDisconnectButton.Click += (_, _) =>
        {
            AppServices.Management?.Disconnect();
            LoadCentralManagementSettings();
        };

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

        WireDebugPageEvents();

#if DEBUG
        VerifyDialogHook();
#endif

#if DEBUG

        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_ICONS") == "1")
{
    DispatcherTimer.RunOnce(() => SaveShot("_icons.png"), TimeSpan.FromSeconds(5));
}
if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_ANIM") == "1")
        {
            DispatcherTimer.RunOnce(() =>
            {
                NavigateTo("appearance");
                DispatcherTimer.RunOnce(() => SaveShot("_anim_early.png"), TimeSpan.FromMilliseconds(60));
                DispatcherTimer.RunOnce(() => SaveShot("_anim_mid.png"), TimeSpan.FromMilliseconds(300));
                DispatcherTimer.RunOnce(() => SaveShot("_anim_late.png"), TimeSpan.FromMilliseconds(900));
            }, TimeSpan.FromSeconds(4));
        }
#endif

        _homeGreetingTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _homeGreetingTimer.Tick += (_, _) => RefreshClassroom();
    }

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
        FontScaleSlider.Value = Math.Clamp(s.FontScale, 0.8, 2.4);
        MainWindowScaleSlider.Value = Math.Clamp(s.MainWindowScale, 0.5, 4.0);
        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x";
        OrientationCombo.SelectedIndex = s.Orientation == LayoutOrientation.Vertical ? 1 : 0;
        ClickThroughSwitch.IsChecked = s.IsClickThrough;

        LoadNotificationSettings();

        LoadBehaviorSettings();
        LoadCentralManagementSettings();

        _widgetSettingsDirty = true;
        if (CurrentPageKey == "widgets") EnsureWidgetSettings();

        RefreshDebugVisibility();

        _loading = false;

    }

    private void SwitchPage()
    {
        if (NavView.SelectedItem is not NavigationViewItem { Tag: string key }) return;
        var index = Array.FindIndex(Sections, section => section.Key == key);
        if (index < 0) return;
        PageScroll.Offset = Vector.Zero;
        if (index == _lastPageIndex) return;
        var previous = _currentPageView;
        if (!_suppressHistory && _lastPageIndex >= 0)
        {
            if (_pageHistory.Count == 64) _pageHistory.RemoveAt(0);
            _pageHistory.Add(_lastPageIndex);
        }
        _lastPageIndex = index;
        BackButton.IsEnabled = _pageHistory.Count != 0;
        if (previous is not null) previous.IsVisible = false;
        _currentPageView = PageFor(index);
        _currentPageView.IsVisible = true;
        PageTitleText.Text = Sections[index].Title;
        PageSubtitleText.Text = Sections[index].Description;
        PageSubtitleText.IsVisible = PageSubtitleText.Text?.Length > 0;
        if (previous is not null) WinUIPageTransitions.FadeIn(_currentPageView);
        if (SearchResultsPanel.IsVisible) SearchBox.Text = "";
        if (key is "home" or "schedule" or "profile") RefreshClassroom();
        if (key is "home" or "schedule") _homeGreetingTimer?.Start();
        else _homeGreetingTimer?.Stop();
        if (key == "nex") { RefreshNexPage(); RefreshNexApiFields(); }
        if (key == "widgets") EnsureWidgetSettings();
        if (key == "plugins") RefreshPlugins();
        if (key == "central") LoadCentralWithoutSaving();
    }

    private void EnsureWidgetSettings()
    {
        if (!_widgetSettingsDirty) return;
        var loading = _loading;
        var selected = WidgetList.SelectedIndex;
        _loading = true;
        try
        {
            if (WidgetLibraryPanel.Children.Count == 0) RefreshWidgetLibrary();
            RefreshWidgetList();
            if (_widgets.Count > 0) WidgetList.SelectedIndex = Math.Clamp(selected, 0, _widgets.Count - 1);
            _widgetSettingsDirty = false;
        }
        finally { _loading = loading; }
    }

    private void SaveContinuousInput()
    {
        if (_editSaveTimer.IsEnabled) { _pendingEdit = AppServices.Settings; return; }
        AppServices.SaveSettings();
        _editSaveTimer.Start();
    }

    private void FlushContinuousInput()
    {
        var pending = _pendingEdit;
        _pendingEdit = null;
        if (pending is not null && ReferenceEquals(pending, AppServices.Settings)) AppServices.SaveSettings();
        else _editSaveTimer.Stop();
    }

    private void LoadCentralWithoutSaving()
    {
        var loading = _loading;
        _loading = true;
        try { LoadCentralManagementSettings(); }
        finally { _loading = loading; }
    }

    private bool _nexEventsWired;
    private bool _nexLoading;
    private DispatcherTimer? _nexGlowPreviewTimer;

    private void PlayNexGlowPreview()
    {
        _nexGlowPreviewTimer?.Stop();
        AppServices.NexVoice?.ShowGlow();
        AppServices.MainWindow?.ShowNexPanel("请讲", true);

        _nexGlowPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        _nexGlowPreviewTimer.Tick += (_, _) =>
        {
            _nexGlowPreviewTimer?.Stop();
            _nexGlowPreviewTimer = null;
            AppServices.NexVoice?.GlowWindow?.StopGlow();
            AppServices.MainWindow?.HideNexPanel();
        };
        _nexGlowPreviewTimer.Start();
    }

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
                AppServices.SaveSettings();

                if (on)
                    AppServices.NexVoice?.Start();
                else
                    AppServices.NexVoice?.Stop();
            };

            NexGlowTestButton.Click += (_, _) => PlayNexGlowPreview();
            NexGlowEntryButton.Click += (_, _) => PlayNexGlowPreview();

            NexEdgeBarCheck.IsCheckedChanged += (_, _) =>
            {
                if (_nexLoading)
                    return;

                AppServices.Settings.IsNexEdgeBarEnabled = NexEdgeBarCheck.IsChecked == true;
                AppServices.SaveSettings();
                AppServices.NexVoice?.RefreshEdgeBar();
            };

            NexEdgeBarSideCombo.SelectionChanged += (_, _) =>
            {
                if (_nexLoading)
                    return;

                AppServices.Settings.NexEdgeBarSide = NexEdgeBarSideCombo.SelectedIndex == 1 ? "left" : "right";
                AppServices.SaveSettings();
                AppServices.NexVoice?.RefreshEdgeBar();
            };

            NexSpeechSettingsButton.Click += (_, _) => App.OpenWindowsSpeechSettings();

            NexSoundSettingsButton.Click += (_, _) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:sound")
                    {
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex)
                {
                    Services.NexVoiceLog.Write("打开声音设置失败：" + ex.Message);
                }
            };

            NexMicTestButton.Click += async (_, _) =>
            {
                NexVoiceStatusText.Text = "正在测试麦克风…（请对着麦克风说一句话）";
                var result = AppServices.NexVoice is null
                    ? "语音唤醒没启用"
                    : await AppServices.NexVoice.TestMicrophoneAsync();
                NexVoiceStatusText.Text = result;
            };

            if (AppServices.NexVoice is { } voice) voice.StatusChanged += VoiceStatusChanged;

            NexSystemPromptBox.TextChanged += (_, _) => SaveNexSettings();
            NexPermissionCombo.SelectionChanged += (_, _) => SaveNexSettings();

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
                AppServices.SaveSettings();
                RefreshNexApiFields();
                NexStatusText.Text = "已恢复默认配置。";
            };
        }

        _nexLoading = true;
        try
        {
            NexSystemPromptBox.Text = s.NexSystemPrompt;
            NexPermissionCombo.SelectedIndex = (int)Services.NexIntelligence.NexPermissionNames.FromKey(s.NexPermission);
            NexVoiceWakeCheck.IsChecked = s.IsNexVoiceWakeEnabled;
            NexEdgeBarCheck.IsChecked = s.IsNexEdgeBarEnabled;
            NexEdgeBarSideCombo.SelectedIndex = s.NexEdgeBarSide == "left" ? 1 : 0;
            NexVoiceStatusText.Text = AppServices.NexVoice?.IsRunning == true ? "正在听「嘿 NEX」…" : "未启用";
            UpdateNexApiStateText();
        }
        finally
        {
            _nexLoading = false;
        }
    }

    private void UpdateNexApiStateText()
    {
        var s = AppServices.Settings;
        var model = Services.NexIntelligence.NexConfig.ResolveModel(s);
        var hasKey = !string.IsNullOrWhiteSpace(Services.NexIntelligence.NexConfig.ResolveApiKey(s));
        NexApiStateText.Text = hasKey ? model : "尚未配置";
        NexApiStateItem.Description = !hasKey ? "请在模型服务配置中填写密钥。" :
            Services.NexIntelligence.NexConfig.IsUsingBuiltInKey(s) ? "使用本机环境凭据" : "使用自定义凭据";
    }

    private void RefreshNexApiFields()
    {
        var s = AppServices.Settings;

        _nexLoading = true;
        try
        {
            NexBaseUrlBox.Text = s.NexApiBaseUrl;
            NexApiKeyBox.Text = s.NexApiKey;              
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
            s.NexModel = "";                              
        else
            s.NexModel = model;

        SaveContinuousInput();
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

        AppServices.SaveSettings();
    }

    private async Task FetchModelsAsync()
    {
        SaveNexApiFields();
        if (string.IsNullOrWhiteSpace(Services.NexIntelligence.NexConfig.ResolveApiKey(AppServices.Settings)))
        {
            NexStatusText.Text = "请先配置接口密钥。";
            return;
        }
        NexStatusText.Text = "正在拉取模型列表…";

        try
        {
            var s = AppServices.Settings;
            var models = await Services.NexIntelligence.NexModelCatalog.FetchAsync(
                Services.NexIntelligence.NexConfig.ResolveBaseUrl(s),
                Services.NexIntelligence.NexConfig.ResolveApiKey(s), _windowLifetime.Token);

            if (_closed) return;
            NexModelCombo.ItemsSource = models;
            var current = Services.NexIntelligence.NexConfig.ResolveModel(s);
            NexModelCombo.SelectedItem = models.Contains(current) ? current : models.FirstOrDefault();
            NexStatusText.Text = $"拉到 {models.Count} 个模型。";
        }
        catch (Exception ex)
        {
            if (!_closed) NexStatusText.Text = "拉取失败：" + ex.Message;
        }
    }

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
                Services.NexIntelligence.NexConfig.ResolveModel(s), _windowLifetime.Token);

            if (_closed) return;
            NexStatusText.Text = "连接正常，模型回复：" + reply;
        }
        catch (Exception ex)
        {
            if (!_closed) NexStatusText.Text = "测试失败：" + ex.Message;
        }
    }

    private DispatcherTimer? _homeGreetingTimer;

    private void RefreshClassroom()
    {
        var profile = AppServices.Schedule.Profile;
        if (CurrentPageKey == "profile")
        {
            ProfileNameText.Text = AppServices.Schedule.FileName;
            ProfilePathText.Text = AppServices.Schedule.FilePath;
            ProfileStatsText.Text = $"{profile.Subjects.Count} 门科目 · {profile.Schedules.Count} 个课表方案";
            return;
        }
        if (CurrentPageKey == "schedule")
        {
            var summary = AppServices.Time.GetTodaySummary(profile, DateTime.Now);
            ScheduleSummaryText.Text = $"{summary.DayText} · {summary.ParityText} · {summary.Slots.Count} 节课";
            ScheduleEmptyText.IsVisible = summary.IsEmpty;
            ScheduleDayList.IsVisible = !summary.IsEmpty;
            ScheduleDayList.ItemsSource = summary.Slots;
            return;
        }
        RefreshHomeGreeting();
    }

    private void RefreshHomeGreeting()
    {
        var now = DateTime.Now;
        var account = AccountService.Current;
        var greeting = now.Hour switch { >= 5 and < 11 => "早上好", >= 11 and < 14 => "中午好", >= 14 and < 18 => "下午好", _ => "晚上好" };
        HomeGreetingText.Text = account is null ? "Hello，请先登录" : $"{greeting}，{account.DisplayName}";
        var summary = AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, now);
        HomeSubText.Text = $"{now:M月d日} · {summary.DayText} · {summary.ParityText}";
        var current = summary.Current;
        var next = summary.Next;
        var focus = current ?? next;
        HomeCourseStatusText.Text = current is not null ? "上课中" : next is not null ? "课前" : summary.IsEmpty ? "无课程" : "已结束";
        HomeCourseNameText.Text = focus?.Subject ?? (summary.IsEmpty ? "今天没有课程" : "今天的课程已结束");
        var remaining = current is null ? "" : $" · 剩余 {Math.Max(1, Math.Ceiling((current.End - now.TimeOfDay).TotalMinutes))} 分钟";
        HomeCourseTimeText.Text = focus is null ? AppServices.Schedule.FileName : $"{focus.TimeRange}{remaining}  {focus.Detail}".TrimEnd();
        var following = current is null && next is not null ? summary.Slots.FirstOrDefault(s => s.Start > next.Start) : next;
        HomeNextText.Text = following is null ? "没有后续课程" : $"{following.Subject} · {following.StartText}";
        var completed = summary.Slots.Count(s => s.End <= now.TimeOfDay);
        HomeStatusDot.IsVisible = current is not null || next is not null;
        HomeDayProgress.Maximum = Math.Max(1, summary.Slots.Count);
        HomeDayProgress.Value = completed;
        HomeTodaySummaryText.Text = $"{completed} / {summary.Slots.Count} 节";
    }

    private void RefreshPlugins()
    {
        var host = AppServices.Plugins;
        PluginRows.Children.Clear();
        PluginRefreshButton.IsEnabled = host is not null && !_pluginsBusy;
        if (host is null) { PluginStatusText.Text = "插件服务尚未就绪。"; return; }
        var plugins = host.LoadedPlugins;
        PluginStatusText.Text = plugins.Count == 0 ? "暂无已安装插件。将可信插件放入插件目录后，选择「重新扫描」。" : $"本机发现 {plugins.Count} 个插件";
        foreach (var plugin in plugins)
        {
            var expander = new SettingsExpander { Header = $"{plugin.Name}  {plugin.Version}", Description = plugin.Status };
            var toggle = new ToggleSwitch { IsChecked = plugin.Enabled, IsEnabled = !_pluginsBusy && plugin.CanToggle && host is ClassNex.Core.Abstractions.IPluginManager };
            Avalonia.Automation.AutomationProperties.SetName(toggle, $"启用 {plugin.Name}");
            toggle.IsCheckedChanged += async (_, _) =>
            {
                if (host is ClassNex.Core.Abstractions.IPluginManager manager)
                    await RunPluginAction(token => manager.SetEnabledAsync(plugin.Id, toggle.IsChecked == true, token));
            };
            expander.Footer = toggle;
            expander.Items.Add(new SettingsExpanderItem { Content = "插件标识", Description = plugin.Id });
            expander.Items.Add(new SettingsExpanderItem { Content = "权限声明", Description = plugin.Permissions.Count > 0 ? string.Join("、", plugin.Permissions) : "未声明" });
            PluginRows.Children.Add(expander);
        }
    }

    private async void OnReloadPlugins(object? sender, RoutedEventArgs e)
    {
        if (AppServices.Plugins is { } host) await RunPluginAction(host.LoadAllAsync);
    }

    private async Task RunPluginAction(Func<CancellationToken, Task> action)
    {
        if (_pluginsBusy || _closed) return;
        _pluginsBusy = true;
        PluginRows.IsEnabled = PluginRefreshButton.IsEnabled = false;
        PluginStatusText.Text = "正在更新插件…";
        string? error = null;
        try { await action(_windowLifetime.Token); }
        catch (OperationCanceledException) when (_closed) { }
        catch (Exception ex) { error = $"无法更新插件：{ex.Message}"; }
        finally
        {
            _pluginsBusy = false;
            if (!_closed)
            {
                PluginRows.IsEnabled = true;
                RefreshPlugins();
                if (error is not null) PluginStatusText.Text = error;
            }
        }
    }

    private void OnOpenPluginsFolder(object? sender, RoutedEventArgs e)
    {
        try { OpenInExplorer(Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "plugins")).FullName); }
        catch (Exception ex) { PluginStatusText.Text = $"无法打开插件目录：{ex.Message}"; }
    }

    private void UpdateSearchResults()
    {
        var query = SearchBox.Text?.Trim() ?? "";
        _searchEntries.Clear();

        if (query.Length > 0)
        {
            foreach (var (title, pageIndex) in SearchIndex)
            {
                if (title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || Sections[pageIndex].Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    _searchEntries.Add(new SearchEntry
                    {
                        Title = title,
                        PageName = Sections[pageIndex].Title,
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

        var key = Sections[entry.PageIndex].Key;
        Dispatcher.UIThread.Post(() =>
        {
            if (_closed) return;
            SearchBox.Text = "";
            NavigateTo(key);
        });
    }

    private void OnOpenDataFolder(object? sender, RoutedEventArgs e)
        => OpenInExplorer(SettingsService.DataDirectory);

    private Control PageFor(int index) => index switch
    {
        0 => PageHome,
        1 => PageGeneral,
        2 => PageInterface,
        3 => PageWindow,
        4 => PageWidgets,
        5 => PageNex,
        6 => PageNotification,
        7 => PageAbout,
        8 => PageDebug,
        9 => PageAccount,
        10 => PageSchedule,
        11 => PageProfile,
        12 => PageCentral,
        13 => PagePlugins,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    private void SaveShot(string fileName)
    {
        try
        {
            var w = (int)Math.Ceiling(Bounds.Width);
            var h = (int)Math.Ceiling(Bounds.Height);
            if (w <= 0 || h <= 0)
                return;

            var target = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new Avalonia.PixelSize(w, h), new Avalonia.Vector(96, 96));
            target.Render(this);
            target.Save(System.IO.Path.Combine(AppContext.BaseDirectory, fileName));
            target.Dispose();
        }
        catch (Exception ex)
        {
            LogCrashSafe(ex);
        }
    }

    private static void LogCrashSafe(Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(SettingsService.DataDirectory, "Logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SaveShot: {ex}{Environment.NewLine}");
        }
        catch
        {

        }
    }

    private void OnOpenNexChat(object? sender, RoutedEventArgs e) => App.OpenNexChat();

    private void OnOpenLogsFolder(object? sender, RoutedEventArgs e)
    {
        SettingsService.EnsureDataDirectory();
        OpenInExplorer(SettingsService.LogsDirectory);
    }

    private void OnOpenAppFolder(object? sender, RoutedEventArgs e)
        => OpenInExplorer(AppContext.BaseDirectory);

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

        }
    }

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

        }
    }

    private void OnOpenLogFile(object? sender, RoutedEventArgs e)
    {
        SettingsService.EnsureDataDirectory();
        OpenInExplorer(System.IO.File.Exists(SettingsService.CrashLogPath)
            ? SettingsService.CrashLogPath
            : SettingsService.LogsDirectory);
    }

    private const string DebugUnlockPhrase = "我知晓我所做的行为开发者不对此负责";

    private int _versionClickCount;

    private async void OnVersionPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var s = AppServices.Settings;
        if (s.IsDebugOptionsEnabled)
            return;

        _versionClickCount++;
        if (_versionClickCount < 10)
            return;

        _versionClickCount = 0;

        if (!await ConfirmDebugUnlockAsync())
            return;

        s.IsDebugOptionsEnabled = true;
        AppServices.SaveSettings();
        RefreshDebugVisibility();
        NavigateTo("debug");
    }

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

    private void WireDebugPageEvents()
    {
        DebugTriggerTestNotificationItem.Click += (_, _) => AppServices.NotificationService?.DebugTriggerTestNotification();
        DebugTriggerClassPreparingItem.Click += (_, _) => AppServices.NotificationService?.DebugTriggerClassPreparing();
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
                Text = "ClassNEX 是一款本地运行的桌面课表悬浮窗。课程表和设置保存在你的电脑上；" +
                       "登录账户后，头像、昵称和邮箱会由 ClassNEX 账户服务同步到本机，仅用于界面展示。" +
                       "应用还会连接 GitHub 获取版本信息与发布包。" +
                       "我们不会收集、出售或与第三方共享你的任何数据。"
            },
            PrimaryButtonText = "知道了",
            DefaultButton = ContentDialogButton.Primary
        }.ShowAsync();
    }

    private const string RepoUrl = "https://github.com/ClassNex/ClassNEX";

    private void OnOpenHomePage(object? sender, RoutedEventArgs e) => OpenUrl(RepoUrl);

    private void OnPreviewOobe(object? sender, RoutedEventArgs e) => App.ShowOobePreview();

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

        }
    }

    private void OnRestartApp(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        }
        catch
        {

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

        }
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

        s.ThemeMode = ThemeCombo.SelectedIndex switch
        {
            1 => "light",
            2 => "dark",
            _ => "system",
        };

        App.ApplyTheme(s.ThemeMode);
        AppServices.SaveSettings();
    }

    private void ApplyInterface(bool continuous = false)
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
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x";

        if (continuous) SaveContinuousInput();
        else AppServices.SaveSettings();
    }

    private void OnOpacityDecrease(object? sender, RoutedEventArgs e) =>
        OpacitySlider.Value = Math.Clamp(OpacitySlider.Value - 0.05, 0.1, 1);

    private void OnOpacityIncrease(object? sender, RoutedEventArgs e) =>
        OpacitySlider.Value = Math.Clamp(OpacitySlider.Value + 0.05, 0.1, 1);

    private void OnFontScaleDecrease(object? sender, RoutedEventArgs e) =>
        FontScaleSlider.Value = Math.Clamp(FontScaleSlider.Value - 0.05, 0.8, 2.4);

    private void OnFontScaleIncrease(object? sender, RoutedEventArgs e) =>
        FontScaleSlider.Value = Math.Clamp(FontScaleSlider.Value + 0.05, 0.8, 2.4);

    private void OnMainScaleDecrease(object? sender, RoutedEventArgs e) =>
        MainWindowScaleSlider.Value = Math.Clamp(MainWindowScaleSlider.Value - 0.05, 0.5, 4.0);

    private void OnMainScaleIncrease(object? sender, RoutedEventArgs e) =>
        MainWindowScaleSlider.Value = Math.Clamp(MainWindowScaleSlider.Value + 0.05, 0.5, 4.0);

    private void OnScalePresetStandard(object? sender, RoutedEventArgs e) =>
        ApplyScalePreset(1.0, 1.9);

    private void OnScalePresetClassroom(object? sender, RoutedEventArgs e) =>
        ApplyScalePreset(1.35, 2.6);

    private void ApplyScalePreset(double fontScale, double mainScale)
    {
        _loading = true;
        FontScaleSlider.Value = fontScale;
        MainWindowScaleSlider.Value = mainScale;
        _loading = false;
        ApplyInterface();
    }

    private sealed record WidgetTypeItem(string DisplayName, string Description, string Glyph, string Type);

    private void RefreshWidgetLibrary()
    {
        WidgetLibraryPanel.Children.Clear();
        foreach (var type in AppServices.Widgets.AvailableTypes)
        {
            var item = new WidgetTypeItem(
                type.DisplayName,
                type.Description,
                type.Glyph,
                type.Type);

            var icon = new AvaloniaFluentUI.Controls.FontIcon
            {
                FontFamily = new Avalonia.Media.FontFamily("avares://ClassNex/Assets/Fonts/#lucide"),
                Glyph = item.Glyph,
                FontSize = 26,
                Width = 32,
                Height = 32,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };

            var title = new TextBlock
            {
                Text = item.DisplayName,
                FontSize = 14,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var description = new TextBlock
            {
                Text = item.Description,
                FontSize = 12,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var text = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { title, description },
            };
            var addIcon = new AvaloniaFluentUI.Controls.FontIcon
            {
                FontFamily = new Avalonia.Media.FontFamily("avares://ClassNex/Assets/Fonts/#lucide"),
                Glyph = "\uE13D",
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.72,
            };
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                ColumnSpacing = 10,
                Children = { icon, text, addIcon },
            };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(addIcon, 2);

            var card = new Button
            {
                Width = 225,
                MinHeight = 58,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(8, 6),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Classes = { "widgetChoice" },
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Content = grid,
            };
            ToolTip.SetTip(card, "添加到主界面");
            card.Click += (_, _) => AddWidget(item.Type);
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
        var loading = _loading;
        _loading = true;

        WidgetEditor.IsEnabled = _currentWidget is not null;

        if (_currentWidget is not null)
        {
            var config = _currentWidget.Config;
            WidgetTypeText.Text = $"{WidgetRegistry.DisplayNameOf(config.Type)} — {WidgetRegistry.DescriptionOf(config.Type)}";
            WidgetEnabledCheck.IsChecked = config.IsEnabled;
            WidgetFontSlider.Value = Math.Clamp(config.FontScale, 0.6, 2.0);
            WidgetFontValueText.Text = $"{config.FontScale:0.00}x";

            WidgetSettingsPanel.Children.Clear();
            foreach (var row in WidgetSettingsBuilder.Build(config, OnWidgetSettingChanged))
                WidgetSettingsPanel.Children.Add(row);

            WidgetNoSettingsText.IsVisible = !WidgetSettingsBuilder.HasSettings(config.Type);
        }

        _loading = loading;
    }

    private ToggleSwitch StartupToggle =>
        (ToggleSwitch)StartupToggleHost.Child!;

    private ToggleSwitch UrlProtocolToggle =>
        (ToggleSwitch)UrlProtocolToggleHost.Child!;

    private ToggleSwitch SafeModeToggle =>
        (ToggleSwitch)SafeModeToggleHost.Child!;

    private ToggleSwitch CentralManagementToggle =>
        (ToggleSwitch)CentralManagementToggleHost.Child!;

    private void LoadBehaviorSettings()
    {
        var s = AppServices.Settings;

        StartupToggle.IsChecked = s.RunAtStartup;
        UrlProtocolToggle.IsChecked = s.RegisterUrlProtocol;
        SafeModeToggle.IsChecked = s.TeachingSafeMode;
        SafeModeCombo.SelectedIndex = Math.Clamp(s.CrashHandlingMode, 0, 2);

        if (_settingEventsWired) return;
        _settingEventsWired = true;

        void WireStartup()
        {
            if (_loading)
                return;
            AppServices.Settings.RunAtStartup = StartupToggle.IsChecked == true;
            AppServices.SaveSettings();
            StartupRegistry.SetRunAtStartup(AppServices.Settings.RunAtStartup);
        }

        StartupToggle.IsCheckedChanged += (_, _) => WireStartup();

        void WireUrl()
        {
            if (_loading)
                return;
            AppServices.Settings.RegisterUrlProtocol = UrlProtocolToggle.IsChecked == true;
            AppServices.SaveSettings();
            StartupRegistry.SetUrlProtocol(AppServices.Settings.RegisterUrlProtocol);
        }

        UrlProtocolToggle.IsCheckedChanged += (_, _) => WireUrl();

        void WireSafe()
        {
            if (_loading)
                return;
            AppServices.Settings.TeachingSafeMode = SafeModeToggle.IsChecked == true;
            AppServices.SaveSettings();
        }

        SafeModeToggle.IsCheckedChanged += (_, _) => WireSafe();

        SafeModeCombo.SelectionChanged += (_, _) =>
        {
            if (_loading)
                return;
            AppServices.Settings.CrashHandlingMode = Math.Clamp(SafeModeCombo.SelectedIndex, 0, 2);
            AppServices.SaveSettings();
        };
    }

    private void LoadCentralManagementSettings()
    {
        var value = AppServices.Settings.CentralManagement;
        CentralManagementToggle.IsChecked = value.IsEnabled;
        CentralServerBox.Text = value.ServerUrl;
        CentralAccountText.Text = AccountService.Current is null
            ? "未登录"
            : AccountService.Current.DisplayName;
        CentralDeviceText.Text = string.IsNullOrWhiteSpace(value.ClientId)
            ? "尚未登记"
            : Environment.MachineName + " · " + value.ClientId[..Math.Min(8, value.ClientId.Length)];
        CentralStatusText.Text = string.IsNullOrWhiteSpace(value.LastStatus)
            ? "未配置"
            : value.LastStatus;
    }

    private void SaveCentralManagementFields()
    {
        if (_loading)
            return;

        var value = AppServices.Settings.CentralManagement;
        value.ServerUrl = CentralServerBox.Text?.Trim() ?? "";
        AppServices.SaveSettings();
    }

    private async Task ConnectCentralManagementAsync()
    {
        SaveCentralManagementFields();
        var value = AppServices.Settings.CentralManagement;
        value.IsEnabled = true;
        CentralManagementToggle.IsChecked = true;
        AppServices.SaveSettings();

        try
        {
            if (AppServices.Management is null)
                throw new InvalidOperationException("集控服务尚未初始化");
            await AppServices.Management.ConnectAndSyncAsync();
            LoadCentralManagementSettings();
        }
        catch (Exception ex)
        {
            CentralStatusText.Text = "连接失败：" + ex.Message;
        }
    }

    private void RefreshCentralAccountUi()
    {
        if (!IsVisible)
            return;

        CentralAccountText.Text = AccountService.Current is null
            ? "未登录"
            : AccountService.Current.DisplayName;
        CentralAccountLoginButton.Content = AccountService.Current is null ? "登录 ClassNEX ID" : "切换账户";
    }

    private void OnCentralManagementStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible)
            {
                CentralStatusText.Text = status;
            }
        });
    }

    private void LoadNotificationSettings()
    {
        var n = AppServices.Settings.Notification;
        PreparingEnabledCheck.IsChecked = n.IsClassOnPreparingNotificationEnabled;
        ClassOnEnabledCheck.IsChecked = n.IsClassOnNotificationEnabled;
        ClassOffEnabledCheck.IsChecked = n.IsClassOffNotificationEnabled;
        ShowTeacherCheck.IsChecked = n.ShowTeacherName;
        IndoorPreparingBox.Value = Math.Clamp(n.ClassOnBreakReminderMinutes, 1, 60);
        OutdoorPreparingBox.Value = Math.Clamp(n.OutDoorClassPreparingDeltaTime, 0, 3600);
        PreparingText.Text = n.ClassOnPreparingText;
        PreparingMaskText.Text = n.ClassOnPreparingMaskText;
        OutdoorPreparingText.Text = n.OutdoorClassOnPreparingText;
        OutdoorPreparingMaskText.Text = n.OutdoorClassOnPreparingMaskText;
        ClassOnMaskBox.Text = n.ClassOnMaskText;
        ClassOffOverlayBox.Text = n.ClassOffOverlayText;

        if (_notificationEventsWired) return;
        _notificationEventsWired = true;

        PreparingEnabledCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ClassOnEnabledCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
        ClassOffEnabledCheck.IsCheckedChanged += (_, _) => SaveNotificationSettings();
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

    private void SaveNotificationSettings()
    {
        if (_loading)
            return;

        var n = AppServices.Settings.Notification;
        n.IsClassOnPreparingNotificationEnabled = PreparingEnabledCheck.IsChecked == true;
        n.IsClassOnNotificationEnabled = ClassOnEnabledCheck.IsChecked == true;
        n.IsClassOffNotificationEnabled = ClassOffEnabledCheck.IsChecked == true;
        n.ShowTeacherName = ShowTeacherCheck.IsChecked == true;
        n.ClassOnBreakReminderMinutes = (int)Math.Clamp(IndoorPreparingBox.Value, 1, 60);
        n.InDoorClassPreparingDeltaTime = n.ClassOnBreakReminderMinutes * 60;
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

    private void OnWidgetItemMore(object? sender, RoutedEventArgs e)
    {

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

    private async void OnOpenTimetable(object? sender, RoutedEventArgs e)
    {
        var path = await FilePickerHelper.PickTimetableAsync(this);
        if (path is null)
            return;

        AppServices.LoadTimetable(path);
    }
}
