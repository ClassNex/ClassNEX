using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using FluentAvalonia.Styling;

namespace ClassNex.Views;

public partial class SettingsWindow : Window
{
    private readonly Action<string>? _onFileLoaded;

    public SettingsWindow() : this(null, "使用内置示例课表")
    {
    }

    public SettingsWindow(Action<string>? onFileLoaded, string currentFile)
    {
        InitializeComponent();
        _onFileLoaded = onFileLoaded;
        CurrentFileText.Text = currentFile;
    }

    private void OnThemeChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb)
            return;

        var theme = App.FluentTheme;
        if (theme is null)
            return;

        if (ReferenceEquals(rb, ThemeLight))
        {
            theme.PreferSystemTheme = false;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
        else if (ReferenceEquals(rb, ThemeDark))
        {
            theme.PreferSystemTheme = false;
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        }
        else
        {
            theme.PreferSystemTheme = true;
        }
    }

    private async void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开课表文件",
            AllowMultiple = false,
        };
        dialog.Filters.Add(new FileDialogFilter { Name = "CSES 课表", Extensions = new List<string> { "yaml", "yml" } });
        dialog.Filters.Add(new FileDialogFilter { Name = "所有文件", Extensions = new List<string> { "*" } });

        var result = await dialog.ShowAsync(this);
        if (result is not { Length: > 0 })
            return;

        var path = result[0];
        CurrentFileText.Text = path;
        _onFileLoaded?.Invoke(path);
    }
}
