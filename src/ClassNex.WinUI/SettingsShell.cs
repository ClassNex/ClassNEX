using System.Runtime.InteropServices;
using ClassNex.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

public sealed partial class NativeSettingsWindow
{
    private bool _commandPending;

    private void ConfigureShellWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor != 0 && GetMonitorInfoW(monitor, ref info))
        {
            var scale = Math.Max(96, GetDpiForWindow(hwnd)) / 96d;
            int Px(int dip) => (int)Math.Round(dip * scale);
            var width = info.Work.Right - info.Work.Left;
            var height = info.Work.Bottom - info.Work.Top;
            if (width > 0 && height > 0)
            {
                var w = Math.Min(Px(1240), Math.Max(1, width - Px(16)));
                var h = Math.Min(Px(620), Math.Max(1, height - Px(16)));
                if (AppWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.PreferredMinimumWidth = Math.Min(w, Px(520));
                    presenter.PreferredMinimumHeight = Math.Min(h, Px(460));
                }
                AppWindow.MoveAndResize(new RectInt32(info.Work.Left + (width - w) / 2, info.Work.Top + (height - h) / 2, w, h));
            }
        }
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        _shell.ActualThemeChanged += (_, _) => MatchCaptionTheme();
        _shell.Loaded += (_, _) => MatchCaptionTheme();
    }

    private void MatchCaptionTheme()
    {
        if (_closed || !AppWindowTitleBar.IsCustomizationSupported()) return;
        AppWindow.TitleBar.PreferredTheme = new AccessibilitySettings().HighContrast ? TitleBarTheme.UseDefaultAppMode
            : _shell.ActualTheme == ElementTheme.Dark ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }

    private Button CreateOptionsMenu()
    {
        var options = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 16 }, Padding = new Thickness(8, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center };
        Identify(options, "MoreOptions", "更多选项");
        ToolTipService.SetToolTip(options, "更多选项");
        static MenuFlyoutItem Item(string label, Action command)
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) => command();
            return item;
        }
        var menu = new MenuFlyout();
        menu.Items.Add(Item("编辑档案", () => _bridge.Command(NativeSettingsKeys.Profile)));
        menu.Items.Add(Item("打开课表文件…", () => RunUserAction(() => _bridge.ExecuteAsync(NativeSettingsKeys.PickTimetable))));
        menu.Items.Add(Item("打开 NEX 对话", () => _bridge.Command(NativeSettingsKeys.Chat)));
        menu.Items.Add(Item("预览首次启动向导", () => _bridge.Command(NativeSettingsKeys.Oobe)));
        menu.Items.Add(new MenuFlyoutSeparator());
        var shortcuts = new MenuFlyoutSubItem { Text = "创建快捷方式" };
        shortcuts.Items.Add(Item("桌面", () => RunUserAction(() => CreateShortcut(Environment.SpecialFolder.DesktopDirectory))));
        shortcuts.Items.Add(Item("开始菜单", () => RunUserAction(() => CreateShortcut(Environment.SpecialFolder.Programs))));
        menu.Items.Add(shortcuts);
        var folders = new MenuFlyoutSubItem { Text = "打开文件夹" };
        folders.Items.Add(Item("数据目录", () => RunUserAction(() => OpenFolder(SettingsService.DataDirectory))));
        folders.Items.Add(Item("日志目录", () => RunUserAction(() => OpenFolder(SettingsService.LogsDirectory))));
        folders.Items.Add(Item("应用目录", () => RunUserAction(() => OpenFolder(AppContext.BaseDirectory))));
        menu.Items.Add(folders);
        menu.Items.Add(Item("查看日志", () => RunUserAction(OpenLog)));
        menu.Items.Add(Item("导出诊断摘要…", () => RunUserAction(ExportSummary)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("重新启动 ClassNEX…", () => RunUserAction(async () =>
        {
            if (!await Confirm("重新启动 ClassNEX？", "桌面课表和应用设置将暂时关闭。", "重新启动")) return;
            CommitInputs();
            _bridge.Command(NativeSettingsKeys.Restart);
        })));
        options.Flyout = menu;
        return options;
    }

    private async void RunUserAction(Func<Task> run)
    {
        if (_commandPending || _closed) return;
        _commandPending = true;
        try { await run(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidOperationException or NotSupportedException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            if (!_closed) await new ContentDialog { XamlRoot = _shell.XamlRoot, Title = "未能完成操作", Content = error.Message, CloseButtonText = "关闭" }.ShowAsync();
        }
        finally { _commandPending = false; }
    }

    private async Task<bool> Confirm(string title, string message, string action) => await new ContentDialog
    {
        XamlRoot = _shell.XamlRoot, Title = title, Content = message, PrimaryButtonText = action, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
    }.ShowAsync() == ContentDialogResult.Primary && !_closed;

    private static async Task OpenFolder(string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"找不到文件夹：{path}");
        if (!await Launcher.LaunchFolderAsync(await StorageFolder.GetFolderFromPathAsync(Path.GetFullPath(path))))
            throw new InvalidOperationException("Windows 未能打开文件夹。");
    }

    private static async Task OpenLog()
    {
        var path = SettingsService.CrashLogPath;
        if (File.Exists(path) && await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(path))) return;
        await OpenFolder(SettingsService.LogsDirectory);
    }

    private async Task ExportSummary()
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = $"ClassNEX-诊断摘要-{DateTime.Now:yyyyMMdd-HHmmss}" };
        picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        var s = _bridge.ReadSettings();
        string[] lines = [
            $"ClassNEX {_bridge.Status(NativeSettingsKeys.Version)}", $"时间：{DateTimeOffset.Now:O}",
            $"系统：{Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})", $"运行时：{RuntimeInformation.FrameworkDescription}",
            $"主题：{s.ThemeMode}", $"已启用组件：{s.Widgets.Count(w => w.IsEnabled)} / {s.Widgets.Count}", $"课堂提醒：{(s.AllowNotification ? "开" : "关")}"
        ];
        await FileIO.WriteTextAsync(file, string.Join(Environment.NewLine, lines));
    }

    private async Task CreateShortcut(Environment.SpecialFolder specialFolder)
    {
        var dir = Environment.GetFolderPath(specialFolder);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) throw new DirectoryNotFoundException("找不到 Windows 的快捷方式目录。");
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定 ClassNEX 的启动路径。");
        var packageRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        if (packageRoot != null && File.Exists(Path.Combine(packageRoot.FullName, "PackageType")) && File.Exists(Path.Combine(packageRoot.FullName, "ClassNex.exe")))
            target = Path.Combine(packageRoot.FullName, "ClassNex.exe");
        var path = Path.Combine(dir, "ClassNEX.lnk");
        if (File.Exists(path) && !await Confirm("替换已有快捷方式？", path, "替换")) return;
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 快捷方式服务不可用。");
        var shell = Activator.CreateInstance(type) ?? throw new InvalidOperationException("无法打开快捷方式服务。");
        object? shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(path);
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).WorkingDirectory = Path.GetDirectoryName(target);
            ((dynamic)shortcut).IconLocation = target + ",0";
            ((dynamic)shortcut).Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal uint Size; internal NativeRect Monitor, Work; internal uint Flags; }
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
}
