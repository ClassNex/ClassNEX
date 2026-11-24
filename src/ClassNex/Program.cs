using System.Runtime.InteropServices;
using Avalonia;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex;

internal static class Program
{
    private const string AppUserModelId = "ClassNex.ClassNEX.Desktop";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string title, uint type);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--preview-migration", StringComparer.Ordinal) && !App.IsPreviewInstallation())
        {
            Environment.ExitCode = 2;
            return;
        }
#if DEBUG
        if (args.FirstOrDefault() is "--review-account" or "--review-ui" or "--review-tray" or "--review-alpha-plugin" &&
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review")))
        {
            Environment.ExitCode = 2;
            return;
        }
#endif
        if (OperatingSystem.IsWindows())
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        AppSettings settings;
        IDisposable? firstRunLease;
        try
        {
            SettingsService.EnsureDataDirectory();
            if (!SettingsService.Store.TryLoadForStartup(out settings, out firstRunLease)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            if (OperatingSystem.IsWindows()) MessageBoxW(0, $"无法读取或锁定 ClassNEX 的配置。请检查数据目录及写入权限；原文件未被替换。\n\n{SettingsService.SettingsPath}\n{ex.Message}", "ClassNEX", 0x10);
            Environment.ExitCode = 1;
            return;
        }
        using (firstRunLease) WinUI.DesktopApplication.Run(() =>
        {
            AppServices.Initialize(settings);
            BuildAvaloniaApp().SetupWithClassicDesktopLifetime(args);
        });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
