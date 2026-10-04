using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;
using FluentAvalonia.Styling;

namespace ClassNex;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private ProfileEditorWindow? _profileEditor;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
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
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static FluentAvaloniaTheme? FluentTheme => (FluentAvaloniaTheme?)Current?.Styles[0];

    /// <summary>
    /// 套用 CI（ClassIsland）配色：把 CI 主色设为 FluentAvalonia 的强调色，
    /// 使全应用的按钮 / 选中 / 焦点等统一为 CI 色调；并把调色板注册为应用资源供 XAML 使用。
    /// </summary>
    private static void ApplyCiPalette()
    {
        var theme = FluentTheme;
        if (theme is not null)
        {
            theme.PreferUserAccentColor = false;
            theme.CustomAccentColor = CiPalette.Primary; // CI PrimaryColor #00BFFF
        }

        if (Current is null)
            return;

        Current.Resources["CiPrimary"] = CiPalette.Primary;
        Current.Resources["CiSecondary"] = CiPalette.Secondary;
        Current.Resources["CiPrimaryBrush"] = CiPalette.PrimaryBrush;
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
