using System.Runtime.InteropServices;
using Avalonia;
using ClassNex.Services;

namespace ClassNex;

internal static class Program
{
    private const string AppUserModelId = "ClassNex.ClassNEX.Desktop";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    [STAThread]
    public static void Main(string[] args)
    {
#if DEBUG
        if (args.FirstOrDefault() is "--review-account" or "--review-ui" or "--review-alpha-plugin" &&
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review")))
        {
            Environment.ExitCode = 2;
            return;
        }
#endif
        if (OperatingSystem.IsWindows())
            _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        
        AppServices.Initialize();

        Environment.ExitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
