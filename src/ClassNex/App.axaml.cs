using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ClassNex.Views;
using FluentAvalonia.Styling;

namespace ClassNex;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>获取全局 FluentAvalonia 主题实例，供设置界面切换主题。</summary>
    public static FluentAvaloniaTheme? FluentTheme =>
        (FluentAvaloniaTheme?)Current?.Styles[0];
}
