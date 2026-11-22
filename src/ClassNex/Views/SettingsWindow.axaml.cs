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

    
    private bool _loading;
    private WidgetItem? _currentWidget;
    private int _lastWidgetTabIndex;

    public SettingsWindow()
    {
        InitializeComponent();
        WidgetList.ItemsSource = _widgets;
        InitShell();
        WireEvents();
        LoadFromSettings();
        AccountService.Changed += OnAccountChanged;
        AppServices.Management?.StatusChanged += OnCentralManagementStatusChanged;
        ActualThemeVariantChanged += OnSettingsThemeChanged;
        Closed += (_, _) =>
        {
            AccountService.Changed -= OnAccountChanged;
            AppServices.NexVoice?.StatusChanged -= OnNexVoiceStatusChanged;
            AppServices.Management?.StatusChanged -= OnCentralManagementStatusChanged;
            ActualThemeVariantChanged -= OnSettingsThemeChanged;
            _homeGreetingTimer?.Stop();
            _nexGlowPreviewTimer?.Stop();
        };
    }

    private void OnAccountChanged() => Dispatcher.UIThread.Post(() =>
    {
        RefreshAccountName();
        RefreshSettingsDashboardState();
        RefreshCentralAccountUi();
    });

    private void OnSettingsThemeChanged(object? sender, EventArgs e)
    {
        RefreshAccountStatusBrushes();
        RefreshAvatarImages();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
#if DEBUG
        StartSettingsVerification();
#endif
        if (TitleBar is { } bar)
            bar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
    }

    private void InitShell()
    {
        var assembly = typeof(SettingsWindow).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        VersionText.Text = AboutVersionText.Text = $"版本 {version}";
        InitSettingsNavigation();
        RefreshAccountName();
    }

    private void RefreshAccountName()
    {
        var profile = AccountService.Current;
        var loggedIn = profile is { IsLoggedIn: true };
        var displayName = loggedIn ? profile!.DisplayName.Trim() : "尚未登录";
        var displayEmail = loggedIn ? profile!.Email.Trim() : "登录后自动同步账户资料";
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = loggedIn ? "ClassNEX 用户" : "尚未登录";
        if (string.IsNullOrWhiteSpace(displayEmail))
            displayEmail = loggedIn ? "未提供邮箱" : "登录后自动同步账户资料";

        AccountPageNameText.Text = displayName;
        AccountPageEmailText.Text = displayEmail;
        AccountPageStatusText.Text = loggedIn ? "已连接" : "未登录";
        AccountPageSyncText.Text = loggedIn
            ? "账户资料已通过浏览器授权同步到本机"
            : "使用浏览器完成登录或注册，头像、用户名和邮箱会自动同步";
        AccountLoginButton.Content = loggedIn ? "切换账户" : "浏览器登录";
        AccountLogoutButton.IsVisible = loggedIn;

        RefreshAccountStatusBrushes();
        RefreshAvatarImages();
    }

    private void RefreshAccountStatusBrushes()
    {
        AccountPageStatusBadge.Background = CiPalette.AccentTint(0.14);
        AccountPageStatusBadge.BorderBrush = CiPalette.AccentTint(0.32);
        AccountPageStatusText.Foreground = CiPalette.AccentBrush();
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

    
    private void OnAccountClick(object? sender, RoutedEventArgs e)
    {
        NavigateTo("account");
    }

    private void RefreshAvatarImages()
    {
        var name = AccountService.Current?.DisplayName ?? "";
        var imagePath = AccountService.AvatarImagePath;

        AccountAvatar.Apply(AccountPageAvatarRing, name, imagePath);
    }

    
    private void WireEvents()
    {
        
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

        
        ResetWidgetButton.Click += (_, _) => ResetWidgets();
        WidgetList.SelectionChanged += (_, _) => SelectWidget();
        _lastWidgetTabIndex = WidgetTabs.SelectedIndex;
        WidgetTabs.SelectionChanged += (_, _) =>
        {
            var index = WidgetTabs.SelectedIndex;
            if (index < 0 || index == _lastWidgetTabIndex)
                return;

            var page = index == 0 ? (Control)WidgetLibraryPage : WidgetEditor;
            var kind = index > _lastWidgetTabIndex
                ? WinUIPageTransitionKind.SlideFromRight
                : WinUIPageTransitionKind.SlideFromLeft;
            _lastWidgetTabIndex = index;
            WinUIPageTransitions.Play(page, kind);
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

        
        RefreshSettingsDashboardState();
        _homeGreetingTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _homeGreetingTimer.Tick += (_, _) =>
        {
            if (PageHome.IsVisible)
                RefreshSettingsDashboardState();
        };
        _homeGreetingTimer.Start();
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

        RefreshWidgetLibrary();
        RefreshWidgetList();

        if (_widgets.Count > 0)
            WidgetList.SelectedIndex = 0;

        RefreshDebugVisibility();

        _loading = false;

        NavigateTo("home");
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

    private void OnNexVoiceStatusChanged(string status) => Dispatcher.UIThread.Post(() => NexVoiceStatusText.Text = status);

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

            NexGlowTestButton.Click += (_, _) => PlayNexGlowPreview();
            NexGlowEntryButton.Click += (_, _) => PlayNexGlowPreview();

            NexEdgeBarCheck.IsCheckedChanged += (_, _) =>
            {
                if (_nexLoading)
                    return;

                AppServices.Settings.IsNexEdgeBarEnabled = NexEdgeBarCheck.IsChecked == true;
                SettingsService.Save(AppServices.Settings);
                AppServices.NexVoice?.RefreshEdgeBar();
            };

            NexEdgeBarSideCombo.SelectionChanged += (_, _) =>
            {
                if (_nexLoading)
                    return;

                AppServices.Settings.NexEdgeBarSide = NexEdgeBarSideCombo.SelectedIndex == 1 ? "left" : "right";
                SettingsService.Save(AppServices.Settings);
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

            AppServices.NexVoice?.StatusChanged += OnNexVoiceStatusChanged;

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
        var usingBuiltIn = Services.NexIntelligence.NexConfig.IsUsingBuiltInKey(s);
        var model = Services.NexIntelligence.NexConfig.ResolveModel(s);
        NexApiStateText.Text = usingBuiltIn
            ? $"内置接口 · {model}"
            : $"自定义接口 · {model}";
        NexApiStateItem.Description = usingBuiltIn
            ? "正在使用内置接口（密钥不出现在界面上）"
            : "正在使用你自己填写的接口配置";
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

    

    private DispatcherTimer? _homeGreetingTimer;

    
    
    private void OnOpenDataFolder(object? sender, RoutedEventArgs e)
        => OpenInExplorer(SettingsService.DataDirectory);

    

    
    
    
    
    
    
    
    
    
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

    private void OnShowAbout(object? sender, RoutedEventArgs e) => NavigateTo("about");

    

    
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
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x";

        AppServices.SaveSettings();
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

    private void ApplyScalePreset(double fontScale, double mainScale)
    {
        _loading = true;
        FontScaleSlider.Value = fontScale;
        MainWindowScaleSlider.Value = mainScale;
        _loading = false;
        ApplyInterface();
    }

    

    
    private void RefreshWidgetLibrary()
    {
        WidgetLibraryPanel.Children.Clear();
        foreach (var item in AppServices.Widgets.AvailableTypes)
        {
            var icon = new AvaloniaFluentUI.Controls.FontIcon
            {
                FontFamily = SettingsIcons.SymbolFont,
                Glyph = SettingsIcons.WidgetGlyph(item.Type),
                Classes = { "settings-glyph" },
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
                FontFamily = SettingsIcons.SymbolFont,
                Glyph = "\uE710",
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
                Classes = { "widget-tile" },
                Tag = item.Type,
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

        _loading = false;
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
            ? Environment.MachineName + " · 尚未登记"
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
        if (AccountService.Current is not null &&
            AppServices.Settings.CentralManagement.IsEnabled)
            _ = ConnectCentralManagementAsync();
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
