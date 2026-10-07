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
            desktop.MainWindow = _mainWindow;
            _mainWindow.Show();

            if (!AppServices.Settings.IsMainWindowVisible)
            {
                Dispatcher.UIThread.Post(() => _mainWindow?.Hide(), DispatcherPriority.Background);
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
            _notificationService.Start();

#if DEBUG
            // 调试：CLASSNEX_VERIFY_NOTIFY=1 时启动后连放三次（3s/8s/13s）重要通知：
            // 水波纹 + 岛上遮罩文字（与真实上课/课间触发完全同一条链路）。
            if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_NOTIFY") == "1")
            {
                foreach (var delay in new[] { 3, 8, 13 })
                {
                    DispatcherTimer.RunOnce(() =>
                    {
                        var center = _mainWindow?.GetIslandCenterOnScreen() ?? new Avalonia.PixelPoint(200, 200);
                        _effectWindow!.PlayEffect(new Controls.NotificationEffects.RippleEffect(center));
                        _mainWindow?.ShowNotificationMask("课间休息");
                        DispatcherTimer.RunOnce(() => _mainWindow?.HideNotificationMask(), TimeSpan.FromSeconds(3));
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

                // CLASSNEX_VERIFY_EDITMODE=1：启动后直接进入编辑模式（核对组件工具条排版）
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_EDITMODE") == "1")
                {
                    Dispatcher.UIThread.Post(() => _mainWindow?.EnterEditMode(), DispatcherPriority.Background);
                }

                // 设 CLASSNEX_VERIFY_SHOT=1：等布局稳定后把已存在的窗口各自渲染成 PNG，
                // 供开发期核对排版（RenderTargetBitmap 直接渲染视觉树，不受其它窗口遮挡影响）
                if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SHOT") == "1")
                {
                    var shotTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    shotTimer.Tick += (_, _) =>
                    {
                        shotTimer.Stop();
                        if (_mainWindow is not null)
                            SaveWindowShot(_mainWindow, "_shot_main.png");
                        if (_settingsWindow is not null)
                            SaveWindowShot(_settingsWindow, "_shot_settings.png");
                        if (_profileEditor is not null)
                            SaveWindowShot(_profileEditor, "_shot_editor.png");
                    };
                    shotTimer.Start();
                }
            }
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static FluentAvaloniaTheme? FluentTheme => (FluentAvaloniaTheme?)Current?.Styles[0];

    /// <summary>把未处理异常写到程序目录下的 _crash.log（排查闪退用）。</summary>
    private static void LogCrash(Exception? ex)
    {
        if (ex is null)
            return;

        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "_crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch
        {
            // 忽略写日志失败
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

    public static void OpenSettings(string page = "general")
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

    // ---------- 托盘事件 ----------

    private void OnTrayIconClick(object? sender, EventArgs e)
    {
        switch (AppServices.Settings.TrayClickBehavior)
        {
            case 1:
                OpenSettings();
                break;
            case 2:
                OpenProfileEditor();
                break;
            default:
                ToggleMainWindow();
                break;
        }
    }

    private void OnTrayToggleMainWindow(object? sender, EventArgs e) => ToggleMainWindow();

    private void OnTrayEditProfile(object? sender, EventArgs e) => OpenProfileEditor(0);

    private void OnTrayChangeClass(object? sender, EventArgs e) => OpenProfileEditor(3);

    private void OnTrayEditMainWindow(object? sender, EventArgs e) => OpenSettings("interface");

    private void OnTrayOpenSettings(object? sender, EventArgs e) => OpenSettings();

    private async void OnTrayLoadTimetable(object? sender, EventArgs e)
    {
        var owner = _mainWindow ?? Lifetime?.MainWindow;
        if (owner is null)
            return;

        var path = await FilePickerHelper.PickTimetableAsync(owner);
        if (path is null)
            return;

        AppServices.LoadTimetable(path);
    }

    private void OnTrayRestart(object? sender, EventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe))
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch
            {
                // 忽略重启失败
            }
        }

        Lifetime?.Shutdown();
    }

    private void OnTrayExit(object? sender, EventArgs e) => ExitApp();
}
