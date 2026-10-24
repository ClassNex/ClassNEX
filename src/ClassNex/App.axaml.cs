using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;
using AvaloniaFluentUI.Styling;

namespace ClassNex;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private ProfileEditorWindow? _profileEditor;
    private Views.TopmostEffectWindow? _effectWindow;
    private Views.OobeWindow? _oobeWindow;
    private Views.NexChatWindow? _nexChatWindow;
    private Services.NexVoiceCoordinator? _nexVoice;
    private Services.NotificationService? _notificationService;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception);
        Dispatcher.UIThread.UnhandledException += (_, e) => LogCrash(e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ApplyCiPalette();
            ApplyTheme(AppServices.Settings.ThemeMode);

            _mainWindow = new MainWindow();

            if (!AppServices.Settings.IsOobeCompleted)
            {
                
                
                
                
                _mainWindow.Opened += (_, _) => _mainWindow?.Hide();

                _oobeWindow = new Views.OobeWindow();
                desktop.MainWindow = _oobeWindow;
                _oobeWindow.Show();
            }
            else
            {
                desktop.MainWindow = _mainWindow;
                _mainWindow.Show();

                if (!AppServices.Settings.IsMainWindowVisible)
                {
                    Dispatcher.UIThread.Post(() => _mainWindow?.Hide(), DispatcherPriority.Background);
                }
            }

            
            _effectWindow = new Views.TopmostEffectWindow();
            _notificationService = new Services.NotificationService(_effectWindow);

            
            
            
            if (_mainWindow is { } mw && mw.Screens.Primary is { } primary)
                _effectWindow.UpdateWindowPos(primary, 1 / primary.Scaling, false);

            Services.AppServices.MainWindow = _mainWindow;
            Services.AppServices.NotificationService = _notificationService;
            _notificationService.Start();

            
            _nexVoice = new Services.NexVoiceCoordinator();
            Services.AppServices.NexVoice = _nexVoice;
            if (AppServices.Settings.IsNexVoiceWakeEnabled)
            {
                DispatcherTimer.RunOnce(() => _nexVoice?.Start(), TimeSpan.FromSeconds(2));
            }

            
            DispatcherTimer.RunOnce(() => _nexVoice?.RefreshEdgeBar(), TimeSpan.FromSeconds(3));

            
            HandleUrlProtocol(Environment.GetCommandLineArgs());

            
            DispatcherTimer.RunOnce(() => _mainWindow?.WarmNotificationMask(), TimeSpan.FromSeconds(1));

#if DEBUG
            
            
            if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_NOTIFY") == "1")
            {
                foreach (var delay in new[] { 3, 8, 13 })
                {
                    DispatcherTimer.RunOnce(() =>
                    {
                        var center = _mainWindow?.GetIslandCenterOnScreen() ?? new Avalonia.PixelPoint(200, 200);
                        _effectWindow!.PlayEffect(new Controls.NotificationEffects.RippleEffect(center));
                        _mainWindow?.ShowNotificationMask("课间休息");

                        var summary = Services.AppServices.Time.GetTodaySummary(
                            Services.AppServices.Schedule.Profile, DateTime.Now);
                        var next = Services.AppServices.Time.GetNextCourse(summary.Slots, DateTime.Now.TimeOfDay);
                        DispatcherTimer.RunOnce(() =>
                        {
                            if (next is { } nxt)
                                _mainWindow?.ShowNotificationOverlay(
                                    "本节课间休息长 20 分钟", nxt.DisplayName, "", nxt.TimeRange);
                            else
                                _mainWindow?.ShowNotificationOverlay(
                                    "本节课间休息", "今日课程已结束。", "", "");
                        }, TimeSpan.FromMilliseconds(1500));
                        DispatcherTimer.RunOnce(() => _mainWindow?.HideNotificationMask(),
                            TimeSpan.FromMilliseconds(3500));
                    }, TimeSpan.FromSeconds(delay));
                }
            }
#endif

#if DEBUG
            
            
            
            
            if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY") == "1")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    WriteVerifyReport();
                    Services.EditorSelfTest.Run();
                }, DispatcherPriority.Background);

                var page = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_PAGE");
                var wantWindows =
                    Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_WINDOWS") == "1"
                    || !string.IsNullOrWhiteSpace(page)
                    || Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SEARCH") == "1";

                if (wantWindows)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        OpenProfileEditor(0);
                        
                        
                        OpenSettings(page ?? "widgets");
                    }, DispatcherPriority.Background);
                }

                
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_CHIME") == "1")
                {
                    DispatcherTimer.RunOnce(() => Services.NexChime.PlayWake(), TimeSpan.FromSeconds(2));
                }

                
                var wakeProbe = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_WAKE");
                if (!string.IsNullOrWhiteSpace(wakeProbe))
                {
                    var wakeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    wakeTimer.Tick += (_, _) =>
                    {
                        wakeTimer.Stop();
                        _nexVoice?.SimulateWake(wakeProbe);
                    };
                    wakeTimer.Start();
                }

                
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_MIC") == "1")
                {
                    var micTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                    micTimer.Tick += async (_, _) =>
                    {
                        micTimer.Stop();
                        var result = _nexVoice is null ? "语音服务没起来" : await _nexVoice.TestMicrophoneAsync();
                        Services.NexVoiceLog.Write("【自检】麦克风：" + result);
                    };
                    micTimer.Start();
                }

                
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_EDITMODE") == "1")
                {
                    Dispatcher.UIThread.Post(() => _mainWindow?.EnterEditMode(), DispatcherPriority.Background);
                }

                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_EDITOR") == "1")
                {
                    DispatcherTimer.RunOnce(() => OpenProfileEditor(0), TimeSpan.FromSeconds(3));
                }

                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_TRAY") == "1")
                {
                    DispatcherTimer.RunOnce(() => OnTrayIconClick(null, EventArgs.Empty), TimeSpan.FromSeconds(3));
                }

                
                var nexProbe = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_NEX");
                if (!string.IsNullOrWhiteSpace(nexProbe))
                {
                    var probeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    probeTimer.Tick += (_, _) =>
                    {
                        probeTimer.Stop();
                        OpenNexChat();
                        Dispatcher.UIThread.Post(
                            () => _nexChatWindow?.VerifySend(nexProbe),
                            DispatcherPriority.Background);
                    };
                    probeTimer.Start();
                }

                
                
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SHOT") == "1")
                {
                    
                    
                    var shotDelay = string.IsNullOrWhiteSpace(
                        Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_NEX"))
                        && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_WAKE"))
                            ? 3
                            : 22;
                    var shotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(shotDelay) };
                    shotTimer.Tick += (_, _) =>
                    {
                        shotTimer.Stop();
                        if (_mainWindow is not null)
                            SaveWindowShot(_mainWindow, "_shot_main.png");
                        if (_settingsWindow is not null)
                            SaveWindowShot(_settingsWindow, "_shot_settings.png");
                        if (_profileEditor is not null)
                            SaveWindowShot(_profileEditor, "_shot_editor.png");
                        if (_oobeWindow is not null)
                            SaveWindowShot(_oobeWindow, "_shot_oobe.png");
                        if (_nexChatWindow is not null)
                            SaveWindowShot(_nexChatWindow, "_shot_nex.png");
                        if (_nexVoice?.GlowWindow is { IsVisible: true } glow)
                            SaveWindowShot(glow, "_shot_glow.png");
                    };
                    shotTimer.Start();
                }
            }
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static FluentAvaloniaTheme? FluentTheme => (FluentAvaloniaTheme?)Current?.Styles[0];

    
    private static void LogCrash(Exception? ex)
    {
        if (ex is null)
            return;

        var path = SettingsService.CrashLogPath;
        try
        {
            Services.SettingsService.EnsureDataDirectory();
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            
        }

        
        try
        {
            if (!AppServices.Settings.TeachingSafeMode)
                return;

            switch (AppServices.Settings.CrashHandlingMode)
            {
                case 0:
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                    break;
                case 2:
                    var exe = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exe))
                        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
                    Environment.Exit(1);
                    break;
            }
        }
        catch
        {
            
        }
    }

    
    
    
    
    
    private static void HandleUrlProtocol(string[] args)
    {
        var url = args.FirstOrDefault(a => a.StartsWith("classnex://", StringComparison.OrdinalIgnoreCase));
        if (url is null || Current is not App app)
            return;

        var action = url["classnex://".Length..].Trim('/').ToLowerInvariant();
        switch (action)
        {
            case "profile":
                OpenProfileEditor();
                break;
            case "show":
                app._mainWindow?.Show();
                break;
            case "hide":
                app._mainWindow?.Hide();
                break;
            case "settings":
            default:
                OpenSettings();
                break;
        }
    }

#if DEBUG
    
    private static void WriteVerifyReport()
    {
        try
        {
            var theme = FluentTheme;
            var path = Path.Combine(AppContext.BaseDirectory, "_verify.log");

            File.WriteAllText(path,
                $"CustomAccentColor   = {theme?.CustomAccentColor} (CI 不设自定义色)\n" +
                $"PreferUserAccentColor = {theme?.PreferUserAccentColor} (CI 行为：跟随系统强调色)\n" +
                $"系统强调色(AccentFillColorDefaultBrush) = {CiPalette.AccentBrush()}\n" +
                $"卡片不透明度         = {AppServices.Settings.BackgroundOpacity} (CI 0.5)\n" +
                $"卡片圆角             = {CiPalette.CardCornerRadius} (CI 8)\n" +
                $"课表文件             = {AppServices.TimetablePath}\n" +
                $"科目数 / 课程数      = {AppServices.Schedule.Profile.Subjects.Count} / {AppServices.Schedule.Profile.Schedules.Sum(s => s.Classes.Count)}\n");

            
            try
            {
                var candidates = new[]
                {
                    "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush",
                    "CardStrokeColorDefaultBrush", "ControlFillColorDefaultBrush", "ControlFillColorSecondaryBrush",
                    "ControlStrokeColorDefaultBrush", "SolidBackgroundFillColorBaseBrush",
                    "SolidBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorTertiaryBrush",
                    "SubtleFillColorSecondaryBrush", "LayerFillColorDefaultBrush",
                    "TextFillColorPrimaryBrush", "TextFillColorSecondaryBrush",
                    "AccentFillColorDefaultBrush", "TextOnAccentFillColorPrimaryBrush",
                    "DividerStrokeColorDefaultBrush", "SystemAccentColor",
                    "SystemControlHighlightListAccentMediumLowBrush", "SolidColorBackgroundBrush",
                    "SystemControlBackgroundAltHighBrush", "SystemControlForegroundBaseHighBrush",
                };

                var lines = new List<string>();
                foreach (var k in candidates)
                {
                    object? v = null;
                    var ok = Current is not null && Current.TryFindResource(k, out v);
                    lines.Add($"{(ok ? "[有]" : "[无]")} {k}{(ok ? $" = {v}" : "")}");
                }

                File.AppendAllText(path, "\n[主题资源键探针]\n" + string.Join("\n", lines) + "\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(path, $"\n[探针失败] {ex.Message}\n");
            }

            
            try
            {
                var fam = new FontFamily("avares://ClassNex/Assets/Fonts/#MiSans");
                var fontLines = new List<string>();
                foreach (var (label, weight) in new[]
                         {
                             ("Normal", FontWeight.Normal),
                             ("Medium", FontWeight.Medium),
                             ("Bold", FontWeight.Bold),
                         })
                {
                    var ok = FontManager.Current.TryGetGlyphTypeface(new Typeface(fam, FontStyle.Normal, weight), out var gt);
                    fontLines.Add($"MiSans {label,-6} = {(ok ? $"{gt!.FamilyName} / weight {gt.Weight}" : "未命中（回退系统字体）")}");
                }

                
                foreach (var name in new[] { "MiSans-Regular.ttf", "MiSans-Bold.ttf" })
                {
                    var fileFamily = new FontFamily($"avares://ClassNex/Assets/Fonts/{name}#MiSans");
                    if (FontManager.Current.TryGetGlyphTypeface(new Typeface(fileFamily), out var fgt))
                        fontLines.Add($"{name,-20} = {fgt.FamilyName} / weight {fgt.Weight}");
                    else
                        fontLines.Add($"{name,-20} = 加载失败");
                }

                File.AppendAllText(path, "\n[MiSans 字体探针]\n" + string.Join("\n", fontLines) + "\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(path, $"\n[字体探针失败] {ex.Message}\n");
            }
        }
        catch
        {
            
        }
    }
#endif

#if DEBUG
    
    
    
    
    private static void SaveWindowShot(Window window, string fileName)
    {
        try
        {
            var width = (int)Math.Ceiling(window.Bounds.Width);
            var height = (int)Math.Ceiling(window.Bounds.Height);
            if (width <= 0 || height <= 0)
                return;

            var target = new Avalonia.Media.Imaging.RenderTargetBitmap(
                new PixelSize(width, height), new Vector(96, 96));
            target.Render(window);
            target.Save(Path.Combine(AppContext.BaseDirectory, fileName));
            target.Dispose();
        }
        catch (Exception ex)
        {
            LogCrash(ex);
        }
    }
#endif

    
    
    
    
    private static void ApplyCiPalette()
    {
        var theme = FluentTheme;
        if (theme is not null)
        {
            
            
            theme.CustomAccentColor = null;
            theme.PreferUserAccentColor = true;
        }

        if (Current is null)
            return;

        Current.Resources["CiCardOpacity"] = CiPalette.CardOpacity;
        Current.Resources["CiCardCornerRadius"] = CiPalette.CardCornerRadius;
        Current.Resources["CiSecondary"] = CiPalette.Secondary;
        Current.Resources["CiNeutralDark"] = CiPalette.NeutralDark;
        Current.Resources["CiOverlaySubtle"] = CiPalette.OverlaySubtle;
        Current.Resources["CiOverlayMedium"] = CiPalette.OverlayMedium;
        Current.Resources["CiHighlight"] = CiPalette.Highlight;
    }

    private static IClassicDesktopStyleApplicationLifetime? Lifetime =>
        Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    
    public static void ApplyTheme(string mode)
    {
        var theme = FluentTheme;
        if (theme is null || Current is null)
            return;

        switch (mode)
        {
            case "light":
                theme.PreferSystemTheme = false;
                Current.RequestedThemeVariant = ThemeVariant.Light;
                break;
            case "dark":
                theme.PreferSystemTheme = false;
                Current.RequestedThemeVariant = ThemeVariant.Dark;
                break;
            default:
                theme.PreferSystemTheme = true;
                break;
        }
    }

    

    public static void ToggleMainWindow()
    {
        if (Current is not App app)
            return;

        var s = AppServices.Settings;

        if (app._mainWindow is { IsVisible: true } visible)
        {
            visible.Hide();
            s.IsMainWindowVisible = false;
        }
        else
        {
            app._mainWindow ??= new MainWindow();
            app._mainWindow.Show();
            app._mainWindow.Activate();
            s.IsMainWindowVisible = true;
        }

        AppServices.SaveSettings();
    }

    public static void OpenSettings(string page = "home")
    {
        if (Current is not App app)
            return;

        if (app._settingsWindow is { IsVisible: true } existing)
        {
            existing.NavigateTo(page);
            existing.Activate();
            return;
        }

        app._settingsWindow = new SettingsWindow();
        app._settingsWindow.Closed += (_, _) => app._settingsWindow = null;
        app._settingsWindow.NavigateTo(page);
        app._settingsWindow.Show();
    }

    public static void OpenProfileEditor(int tabIndex = 0)
    {
        if (Current is not App app)
            return;

        if (app._profileEditor is { IsVisible: true } existing)
        {
            existing.SelectTab(tabIndex);
            existing.Activate();
            return;
        }

        app._profileEditor = new ProfileEditorWindow();
        app._profileEditor.Closed += (_, _) => app._profileEditor = null;
        app._profileEditor.SelectTab(tabIndex);
        app._profileEditor.Show();
    }

    public static void ExitApp()
    {
        if (Current is App app)
            app._mainWindow?.Hide();

        Lifetime?.Shutdown();
    }

    
    
    
    
    public static void OpenWindowsSpeechSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:privacy-speech") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Services.NexVoiceLog.Write("打开系统语音设置失败：" + ex.Message);
        }
    }

    
    public static void OpenNexChat()
    {
        if (Current is not App app)
            return;

        if (app._nexChatWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        app._nexChatWindow = new Views.NexChatWindow();
        app._nexChatWindow.Show();
        app._nexChatWindow.Activate();
    }

    
    public static void RestartApplication()    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        }
        catch
        {
            
        }

        ExitApp();
    }

    

    private Views.TrayMenuWindow? _trayMenu;

    private DispatcherTimer? _trayDebounce;

    
    
    
    
    private void OnTrayIconClick(object? sender, EventArgs e)
    {
        
        _trayDebounce?.Stop();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            OpenTrayMenuOnce();
        };
        _trayDebounce = t;
        t.Start();
    }

    private void OpenTrayMenuOnce()
    {
        if (_trayMenu is { } openMenu)
        {
            openMenu.Close();
            _trayMenu = null;
            return;
        }

        var menu = new Views.TrayMenuWindow();
        menu.Build(new[]
        {
            new Views.TrayMenuWindow.Item("\uE7C4", "显示 / 隐藏主界面", ToggleMainWindow),
            Views.TrayMenuWindow.Separator,
            new Views.TrayMenuWindow.Item("\uE70F", "编辑档案…", () => OpenProfileEditor(0)),
            new Views.TrayMenuWindow.Item("\uE8B7", "加载课表…", () => _ = LoadTimetableFromMenuAsync()),
            new Views.TrayMenuWindow.Item("\uE8AB", "换课…", () => OpenProfileEditor(3)),
            Views.TrayMenuWindow.Separator,
            new Views.TrayMenuWindow.Item("\uE71D", "编辑主界面…", () => OpenSettings("interface")),
            new Views.TrayMenuWindow.Item("\uE713", "应用设置…", () => OpenSettings()),
            Views.TrayMenuWindow.Separator,
            
            
            new Views.TrayMenuWindow.Item("\uF0F7", "NEX Intelligence…", OpenNexChat,
                "avares://ClassNex/Assets/Fonts/#FluentSystemIcons-Resizable"),
            Views.TrayMenuWindow.Separator,
            new Views.TrayMenuWindow.Item("\uE777", "重启 ClassNEX", RestartApp),
            new Views.TrayMenuWindow.Item("\uE7E8", "退出", ExitApp),
        });

        
        
        
        var cursor = TryGetCursorPos(out var pt) ? pt : default;
        var hasCursor = cursor != default;

        menu.Opacity = 0;
        if (hasCursor)
        {
            
            menu.Position = new PixelPoint(
                Math.Max(0, cursor.X - 120),
                Math.Max(0, cursor.Y - 320));
        }

        menu.Show();
        menu.Activate();

        
        Dispatcher.UIThread.Post(() =>
        {
            if (hasCursor && menu.IsVisible)
            {
                var w = menu.Bounds.Width > 0 ? menu.Bounds.Width : 240;
                var h = menu.Bounds.Height > 0 ? menu.Bounds.Height : 320;
                menu.Position = new PixelPoint(
                    Math.Max(0, (int)(cursor.X - w / 2)),
                    Math.Max(0, (int)(cursor.Y - h - 8)));
            }

            menu.Opacity = 1;
        }, DispatcherPriority.Background);

        _trayMenu = menu;
        menu.Closed += (_, _) => _trayMenu = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    private static bool TryGetCursorPos(out PixelPoint point)
    {
        point = default;
        try
        {
            if (!OperatingSystem.IsWindows() || !GetCursorPos(out var p))
                return false;

            point = new PixelPoint(p.X, p.Y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    
    private void RestartApp()
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        }
        catch
        {
            
        }

        ExitApp();
    }

    
    private async Task LoadTimetableFromMenuAsync()
    {
        var owner = _mainWindow ?? Lifetime?.MainWindow;
        if (owner is null)
            return;

        var path = await Services.FilePickerHelper.PickTimetableAsync(owner);
        if (path is null)
            return;

        Services.AppServices.LoadTimetable(path);
    }
}
