using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class MainWindow
{
    private Flyout BuildQuickAppearance()
    {
        var panel = new StackPanel { Spacing = 8, MinWidth = 216 };
        var flyout = new Flyout { Content = panel };
        panel.Children.Add(new TextBlock { Text = "桌面外观", FontSize = 16 });
        QuickSlider(panel, flyout, "主界面缩放", .5, 4, .05, () => AppServices.Settings.MainWindowScale,
            v => { AppServices.Settings.MainWindowScale = v; AppServices.SaveSettings(); }, v =>
            {
                if (RootScale.LayoutTransform is ScaleTransform scale)
                    scale.ScaleX = scale.ScaleY = Math.Clamp(v * AppServices.Settings.FontScale, .4, 5);
            });
        QuickSlider(panel, flyout, "背景不透明度", 0, 1, .01, () => AppServices.Settings.BackgroundOpacity,
            v => { AppServices.Settings.BackgroundOpacity = v; AppServices.SaveSettings(); }, v => IslandBackground.Opacity = v);
        var more = new Button { Content = "更多外观设置", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        more.Click += (_, _) => { flyout.Hide(); App.OpenSettings("appearance"); };
        panel.Children.Add(more);
        return flyout;
    }

    private static void QuickSlider(StackPanel panel, Flyout flyout, string name, double min, double max, double step,
        Func<double> read, Action<double> commit, Action<double>? preview = null)
    {
        var label = new TextBlock();
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, SmallChange = step, IsSnapToTickEnabled = true, Value = read() };
        Avalonia.Automation.AutomationProperties.SetName(slider, name);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        var pending = false;
        void Save()
        {
            timer.Stop();
            if (!pending) return;
            pending = false;
            if (Math.Abs(read() - slider.Value) > .001) commit(slider.Value);
        }
        void Label() => label.Text = $"{name} · {slider.Value:P0}";
        Label();
        timer.Tick += (_, _) => Save();
        slider.ValueChanged += (_, _) => { Label(); preview?.Invoke(slider.Value); pending = true; timer.Stop(); timer.Start(); };
        slider.AddHandler(PointerReleasedEvent, (_, _) => Save(), RoutingStrategies.Bubble, true);
        slider.KeyUp += (_, _) => Save(); slider.LostFocus += (_, _) => Save(); flyout.Closed += (_, _) => Save();
        panel.Children.Add(label); panel.Children.Add(slider);
    }
}
