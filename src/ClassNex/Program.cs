using Avalonia;
using ClassNex.Services;

namespace ClassNex;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        
        AppServices.Initialize();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
