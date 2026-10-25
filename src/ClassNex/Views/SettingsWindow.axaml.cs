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





public partial class SettingsWindow : AppWindow
{
    private readonly ObservableCollection<WidgetItem> _widgets = new();
    private readonly ObservableCollection<SearchEntry> _searchEntries = new();

    
    private readonly List<int> _pageHistory = new();
    private int _lastPageIndex = -1;

    private bool _loading;
    private WidgetItem? _currentWidget;

    
    private sealed class SearchEntry
    {
        public string Title { get; init; } = "";

        public string PageName { get; init; } = "";

        public int PageIndex { get; init; }
    }

    
    private static readonly string[] PageNames = { "主页", "基本", "外观", "窗口", "组件", "NEX Intelligence", "提醒", "关于 ClassNEX", "调试", "账户" };

    
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
        AccountService.Changed += () =>
            Dispatcher.UIThread.Post(RefreshAccountName, DispatcherPriority.Background);

        
        
        
        
        AccountPanel.SizeChanged += (_, e) => ApplyAccountCompact(e.NewSize.Width < 150);
    }

    

    private bool _accountCompact;

    
    
    
    
    
    
    private void ApplyAccountCompact(bool compact)
    {
        if (_accountCompact == compact)
            return;

        _accountCompact = compact;

        if (compact)
        {
            
            
            
            
            
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

    
    private TranslateTransform AccountShift => (TranslateTransform)AccountPanel.RenderTransform!;

    
    
    
    
    
    
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
                WindowTransparencyLevel.AcrylicBlur,
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

        UserNameBox.Text = AppServices.Settings.UserName;
        EmailBox.Text = AppServices.Settings.Email;
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
        var name = profile?.DisplayName ?? AppServices.Settings.UserName;
        var email = profile?.Email ?? AppServices.Settings.Email;
        var displayName = string.IsNullOrWhiteSpace(name) ? "未设置用户名" : name.Trim();
        var displayEmail = string.IsNullOrWhiteSpace(email) ? "未设置邮箱" : email.Trim();

        AccountNameText.Text = displayName;
        AccountEmailText.Text = displayEmail;
        AccountPageNameText.Text = displayName;
        AccountPageEmailText.Text = displayEmail;
        AccountLogoutButton.IsEnabled = profile is not null;

        
        if (CiPalette.TryResource("AccentFillColorDefaultBrush", out var accentBrush) &&
            accentBrush is ISolidColorBrush solid)
        {
            AccountAvatarRing.BorderBrush = new SolidColorBrush(solid.Color);
            AccountPageAvatarRing.BorderBrush = new SolidColorBrush(solid.Color);
        }

        RefreshAvatarImages();
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

    
    private static Color Mix(Color c, Color target, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)Math.Clamp(c.R + (target.R - c.R) * t, 0, 255),
            (byte)Math.Clamp(c.G + (target.G - c.G) * t, 0, 255),
            (byte)Math.Clamp(c.B + (target.B - c.B) * t, 0, 255));
    }

    
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

        double R = l, G = l, B = l;   
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

    
    private static string CustomAvatarPath =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "data", "avatar.png");

    private const string DefaultAvatarUri = "avares://ClassNex/Assets/avatar.png";

    
    
    private void RefreshAvatarImages()
    {
        try
        {
            Avalonia.Media.Imaging.Bitmap bitmap;
            if (AccountService.AvatarImagePath is { } accountAvatar &&
                System.IO.File.Exists(accountAvatar))
            {
                using var stream = System.IO.File.OpenRead(accountAvatar);
                bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }
            else if (System.IO.File.Exists(CustomAvatarPath))
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

        
        if (page == "account")
        {
            PageHome.IsVisible = PageGeneral.IsVisible = PageInterface.IsVisible = PageWindow.IsVisible =
                PageWidgets.IsVisible = PageNotification.IsVisible = PageAbout.IsVisible =
                PageDebug.IsVisible = PageNex.IsVisible = false;
            PageAccount.IsVisible = true;
            PageTitleText.Text = "账户";
            return;
        }

        
        
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
        
        SearchBox.TextChanged += (_, _) => UpdateSearchResults();
        SearchResults.SelectionChanged += (_, _) => NavigateFromSearch();

        NavView.SelectionChanged += (_, _) => SwitchPage();

        
        SingleWeekStartPicker.PropertyChanged += (_, e) =>
        {
            if (!_loading && e.Property.Name == "SelectedDate")
                ApplyGeneral();
        };
        TrayBehaviorCombo.SelectionChanged += (_, _) => ApplyGeneral();

        
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

        
        WidgetLibraryList.DoubleTapped += (_, _) =>
        {
            if (WidgetLibraryList.SelectedItem is WidgetTypeItem item)
                AddWidget(item.Type);
        };
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
                DispatcherTimer.RunOnce(() => SaveShot("_anim_late.png"), TimeSpan.FromMilliseconds(900));
            }, TimeSpan.FromSeconds(4));
        }
#endif

        
        RefreshHomeGreeting();
        _homeGreetingTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _homeGreetingTimer.Tick += (_, _) =>
        {
            if (PageHome.IsVisible)
                RefreshHomeGreeting();
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
        FontScaleSlider.Value = Math.Clamp(s.FontScale, 0.8, 1.6);
        MainWindowScaleSlider.Value = Math.Clamp(s.MainWindowScale, 0.6, 3.0);
        OpacityValueText.Text = $"{s.BackgroundOpacity:P0}";
        FontScaleValueText.Text = $"{s.FontScale:0.00}x";
        MainWindowScaleValueText.Text = $"{s.MainWindowScale:0.00}x（CI 1.9）";
        OrientationCombo.SelectedIndex = s.Orientation == LayoutOrientation.Vertical ? 1 : 0;
        ClickThroughSwitch.IsChecked = s.IsClickThrough;

        
        LoadNotificationSettings();

        
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
        
        
        var name = (NavView.SelectedItem as NavigationViewItem)?.Content as string ?? "";
        var index = Array.IndexOf(PageNames, name);
        if (index < 0)
            index = 0;

        
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
        PageAccount.IsVisible = false;   

        
        PlayPageEntrance();

        
        PageTitleText.Text = PageNames[Math.Clamp(index, 0, PageNames.Length - 1)];

        if (index == 0)
            RefreshHomeGreeting();

        if (index == 5)
            RefreshNexPage();

        
        if (index == 8)
            RefreshNexApiFields();
    }

    

    private bool _nexEventsWired;
    private bool _nexLoading;

    
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
                
                AppServices.NexVoice?.ShowGlow();
                AppServices.MainWindow?.ShowNexPanel("请讲", true);
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    AppServices.NexVoice?.ShowGlow();     
                    AppServices.MainWindow?.HideNexPanel();
                };
                timer.Start();
            };

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

            AppServices.NexVoice?.StatusChanged += status =>
                Dispatcher.UIThread.Post(() => NexVoiceStatusText.Text = status);

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

        
        HomeGreetingText.Text = $"{greeting}, {name}";

        var slots = AppServices.Time.GetTodaySlots(AppServices.Schedule.Profile, now);
        HomeSubText.Text = $"{now:yyyy年M月d日 dddd} · 今日 {slots.Count} 节课";
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

        
        
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (pageIndex >= 0 && pageIndex < NavView.MenuItems.Count)
                    NavView.SelectedItem = NavView.MenuItems[pageIndex];
            }
            catch
            {
                
            }

            SearchResultsPanel.IsVisible = false;
            _searchEntries.Clear();
            SearchBox.Text = "";
        }, DispatcherPriority.Background);
    }

    

    private void OnOpenDataFolder(object? sender, RoutedEventArgs e)
        => OpenInExplorer(SettingsService.DataDirectory);

    

    
    
    
    
    
    
    
    
    
    private void PlayPageEntrance()
    {
        if (PageScroll.RenderTransform is not TranslateTransform shift)
            return;

        const double from = 28;
        const double durationMs = 320;

        var started = DateTime.UtcNow;
        shift.Y = from;

        
        
        
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
            var t = Math.Clamp(elapsed / durationMs, 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);   

            if (PageScroll.RenderTransform is TranslateTransform s)
                s.Y = from * (1 - eased);

            if (t >= 1)
            {
                timer.Stop();
                if (PageScroll.RenderTransform is TranslateTransform done)
                    done.Y = 0;
            }
        };
        timer.Start();

        
        DispatcherTimer.RunOnce(() =>
        {
            timer.Stop();
            if (PageScroll.RenderTransform is TranslateTransform s)
                s.Y = 0;
        }, TimeSpan.FromMilliseconds(durationMs + 200));
    }

    
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
