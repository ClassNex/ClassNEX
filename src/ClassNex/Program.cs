using Avalonia;
using ClassNex.Services;

namespace ClassNex;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 先加载应用设置与课表，再启动 UI
        AppServices.Initialize();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
