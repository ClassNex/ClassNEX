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
        // 全局未处理异常 → 写 _crash.log，便于排查闪退
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogCrash(e.ExceptionObject as Exception);
        Dispatcher.UIThread.UnhandledException += (_, e) => LogCrash(e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogCrash(e.Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 托盘常驻：关闭所有窗口也不退出应用
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ApplyCiPalette();
            ApplyTheme(AppServices.Settings.ThemeMode);

            _mainWindow = new MainWindow();

            if (!AppServices.Settings.IsOobeCompleted)
            {
                // 全新安装：先走首次启动向导，不显示课表浮窗（用户要求）。
                // 向导走完会写 IsOobeCompleted 并重启应用，重启后走下面的正常分支显示浮窗。
                // ★ 必须盯住主窗口的 Opened：Avalonia 会在框架初始化完成后自动 Show 掉
                //   desktop.MainWindow 指向的窗口，只在启动时 Hide 一次是不够的。
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

            // ============ 通知：CI 的全局水波纹（1:1 移植；ClassWidgets 灵动通知暂不接线） ============
            _effectWindow = new Views.TopmostEffectWindow();
            _notificationService = new Services.NotificationService(_effectWindow);

            // 效果窗口铺满主屏工作区。
            // ★ 对照 CI MainWindow.axaml.cs:929 —— scale 传 1/dpiX（把物理像素换算成 DIP），
            //   传 1.0 会让窗口（和波纹半径）比屏幕大 1.25 倍（用户反馈「大小不对」的根因）。
            if (_mainWindow is { } mw && mw.Screens.Primary is { } primary)
                _effectWindow.UpdateWindowPos(primary, 1 / primary.Scaling, false);

            Services.AppServices.MainWindow = _mainWindow;
            Services.AppServices.NotificationService = _notificationService;
            _notificationService.Start();

            // NEX 语音唤醒（「嘿 NEX」→ 提示音 + 跑马灯 + 浮窗翻页变对话）
            _nexVoice = new Services.NexVoiceCoordinator();
            Services.AppServices.NexVoice = _nexVoice;
            if (AppServices.Settings.IsNexVoiceWakeEnabled)
            {
                DispatcherTimer.RunOnce(() => _nexVoice?.Start(), TimeSpan.FromSeconds(2));
            }

            // 处理 classnex:// Url 协议参数（第三方应用/网页调用本应用）
            HandleUrlProtocol(Environment.GetCommandLineArgs());

            // 预热提醒动画（斜切条纹首次触发要 JIT/编译几何，启动 1 秒后静默开合一次，之后不卡）
            DispatcherTimer.RunOnce(() => _mainWindow?.WarmNotificationMask(), TimeSpan.FromSeconds(1));

#if DEBUG
            // 调试：CLASSNEX_VERIFY_NOTIFY=1 时启动后连放三次（3s/8s/13s）重要通知，
            // 与真实下课提醒同链路：面具 1.5s → Overlay「下节课是…」→ 3.5s 收起。
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
            // 调试自检（仅 Debug 构建）：设 CLASSNEX_VERIFY=1 时写 _verify.log 并跑数据自检。
            // ★ 默认**只启动浮窗主界面**，不弹「应用设置 / 档案编辑器」——
            //   需要那两个窗口时才加 CLASSNEX_VERIFY_WINDOWS=1，
            //   或者用 CLASSNEX_VERIFY_PAGE / CLASSNEX_VERIFY_SEARCH（这两个模式本身需要设置窗口）。
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
                        // 自检打开设置窗口；用 CLASSNEX_VERIFY_PAGE 指定页面
                        // （general/interface/widgets/schedule/about/account）
                        OpenSettings(page ?? "widgets");
                    }, DispatcherPriority.Background);
                }

                // CLASSNEX_VERIFY_WAKE=<问题>：模拟一次语音唤醒（提示音 + 跑马灯 + 浮窗翻页 + 提问）
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

                // CLASSNEX_VERIFY_MIC=1：自检麦克风（权限 + 一次性识别），结果写 nex-voice.log
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

                // CLASSNEX_VERIFY_EDITMODE=1：启动后直接进入编辑模式（核对组件工具条排版）
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_EDITMODE") == "1")
                {
                    Dispatcher.UIThread.Post(() => _mainWindow?.EnterEditMode(), DispatcherPriority.Background);
                }

                // CLASSNEX_VERIFY_NEX=<文本>：打开 NEX 对话框并自动发一条消息（核对流式/思考/工具调用）
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

                // 设 CLASSNEX_VERIFY_SHOT=1：等布局稳定后把已存在的窗口各自渲染成 PNG，
                // 供开发期核对排版（RenderTargetBitmap 直接渲染视觉树，不受其它窗口遮挡影响）
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SHOT") == "1")
                {
                    // 等布局稳定后把已存在的窗口各自渲染成 PNG；
                    // 有 NEX 自检（CLASSNEX_VERIFY_NEX）时多等一会儿，好让对话跑完再出图。
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

    /// <summary>把未处理异常写到数据目录 Logs\crash.log（排查闪退用），并按「教学安全模式」处理。</summary>
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
            // 忽略写日志失败
        }

        // 教学安全模式：0=显示崩溃报告（打开日志）1=忽略并继续 2=重新启动应用
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
            // 安全模式的兜底动作失败时忽略（不能因为兜底再抛异常）
        }
    }

    /// <summary>
    /// 响应 classnex:// Url 协议（在设置里开启「注册 Url 协议」后，第三方应用或网页可调用）。
    /// 支持：classnex://settings 打开设置、classnex://profile 打开档案编辑器、
    /// classnex://show 显示主界面、classnex://hide 隐藏主界面。
    /// </summary>
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
    /// <summary>把配色与设置的关键运行时状态写到输出目录 _verify.log，供开发期核对。</summary>
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

            // 主题资源键探针：逐个问候选键是否存在（AvaloniaFluentUI 主题的键名与 FluentAvaloniaUI 未必一致）
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

            // MiSans 字体探针：确认字体集合注册成功、Bold 是真实字重（而非合成加粗）
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

                // 文件级探针：对每个 ttf 单独建 FontFamily（单文件集合），看各自解析出的字重（排查字重错位）
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
            // 自检失败忽略
        }
    }
#endif

#if DEBUG
    /// <summary>
    /// 调试用：把窗口的视觉树直接渲染成 PNG 存到程序目录（不受其它窗口遮挡影响）。
    /// 用于开发期核对排版（SettingsWindow 常被别的窗口盖住，屏幕截图取不到）。
    /// </summary>
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

    /// <summary>
    /// 套用 CI（ClassIsland）配色：把 CI 主色设为 FluentAvalonia 的强调色，
    /// 使全应用的按钮 / 选中 / 焦点等统一为 CI 色调；并把调色板注册为应用资源供 XAML 使用。
    /// </summary>
    private static void ApplyCiPalette()
    {
        var theme = FluentTheme;
        if (theme is not null)
        {
            // 与 CI 完全一致：**不写死强调色**，跟随 Windows 系统强调色
            // （CI 的 FluentTheme/Styles.axaml 里没有任何硬编码颜色，只用 FluentAvalonia 标准资源键）
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

    /// <summary>应用主题：system / light / dark。</summary>
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

    // ---------- 窗口管理 ----------

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

    /// <summary>
    /// 打开 Windows 的语音设置页（ms-settings:privacy-speech）。
    /// 整句语音听写需要用户在那里接受语音隐私政策/开启「在线语音识别」，否则 WinRT 识别会报 0x80045509。
    /// </summary>
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

    /// <summary>打开 NEX Intelligence 对话框（设置页按钮 / 以后可以挂托盘、快捷键）。</summary>
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

    /// <summary>重启应用（首次启动向导完成、设置页「重启 ClassNEX」、托盘菜单「重启」用）。</summary>
    public static void RestartApplication()    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        }
        catch
        {
            // 忽略重启失败
        }

        ExitApp();
    }

    // ---------- 托盘事件 ----------

    private Views.TrayMenuWindow? _trayMenu;

    /// <summary>
    /// 托盘左键 → 弹出 Fluent 菜单（用户要求：左键点开、不要右键弹菜单）。
    /// 原生托盘菜单只能由右键触发，所以这里自己画一个无边框菜单窗；再点一次收起。
    /// </summary>
    private void OnTrayIconClick(object? sender, EventArgs e)
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
            new Views.TrayMenuWindow.Item("\uE777", "重启 ClassNEX", RestartApp),
            new Views.TrayMenuWindow.Item("\uE7E8", "退出", ExitApp),
        });

        menu.Show();
        menu.Activate();

        if (TryGetCursorPos(out var pt))
        {
            // 等一次布局再定位（SizeToContent 的窗口此时才拿到真实尺寸）；菜单出现在光标左下，跟系统菜单一致
            Dispatcher.UIThread.Post(() =>
            {
                var w = menu.Bounds.Width > 0 ? menu.Bounds.Width : 240;
                var h = menu.Bounds.Height > 0 ? menu.Bounds.Height : 320;
                menu.Position = new PixelPoint(
                    Math.Max(0, (int)(pt.X - w / 2)),
                    Math.Max(0, (int)(pt.Y - h - 8)));
            }, DispatcherPriority.Background);
        }

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

    /// <summary>重启 / 退出（托盘菜单用；原来是 NativeMenuItem 的事件处理器）。</summary>
    private void RestartApp()
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.ProcessPath))
                Process.Start(new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true });
        }
        catch
        {
            // 忽略
        }

        ExitApp();
    }

    /// <summary>托盘菜单「加载课表…」。</summary>
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
