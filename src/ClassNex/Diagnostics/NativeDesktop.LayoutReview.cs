#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    internal async Task ReviewSettingsLayoutAsync(string directory, Action<bool, string> check)
    {
        var window = Settings ?? throw new InvalidOperationException("No WinUI settings window");
        var root = (FrameworkElement)window.Content;
        var initial = window.AppWindow.Size;
        var oldTheme = AppServices.Settings.ThemeMode;
        using var log = new StreamWriter(Stream.Null);
        var capture = new NativeVerification(this, log);
        IEnumerable<DependencyObject> Tree(DependencyObject node)
        {
            yield return node;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Tree(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        async Task Shot(string name)
        {
            var path = Path.Combine(directory, name + ".png");
            await capture.Snapshot(path, () => root, true);
            check(File.Exists(path) && new FileInfo(path).Length > 2000, $"Rendered native settings: {name} ({root.XamlRoot.RasterizationScale:P0})");
        }
        try
        {
            var grid = (Grid)root;
            var frame = Tree(root).OfType<Frame>().Single();
            window.SetSystemEffectsForReview(advancedEffects: false);
            check(window.SystemBackdrop is null && grid.Background is SolidColorBrush { Color.A: 255 }, "Transparency off uses an opaque theme surface (per-window policy)");
            window.SetSystemEffectsForReview(highContrast: true);
            check(window.SystemBackdrop is null && grid.Background is SolidColorBrush contrast && contrast.Color == new Windows.UI.ViewManagement.UISettings().UIElementColor(Windows.UI.ViewManagement.UIElementType.Window), "High-contrast policy uses the system window colour");
            window.SetSystemEffectsForReview(micaSupported: false);
            check(window.SystemBackdrop is null && grid.Background is SolidColorBrush { Color.A: 255 }, "Unsupported Mica falls back without a fabricated backdrop");
            window.SetSystemEffectsForReview(animationsEnabled: false);
            Microsoft.UI.Xaml.Media.Animation.NavigationTransitionInfo? transition = null;
            void Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs args) => transition = args.NavigationTransitionInfo;
            frame.Navigated += Navigated;
            try
            {
                window.NavigateTo("privacy");
                check(transition is Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo, "Reduced motion suppresses top-level navigation");
                window.NavigateTo("schedule");
                check(transition is Microsoft.UI.Xaml.Media.Animation.SuppressNavigationTransitionInfo, "Reduced motion suppresses detail navigation");
            }
            finally { frame.Navigated -= Navigated; window.SetSystemEffectsForReview(); }
            var actualMica = Microsoft.UI.Composition.SystemBackdrops.MicaController.IsSupported() && new Windows.UI.ViewManagement.UISettings().AdvancedEffectsEnabled && !new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
            check(actualMica ? window.SystemBackdrop is MicaBackdrop && grid.Background is SolidColorBrush { Color.A: 0 } : window.SystemBackdrop is null, $"Real Windows policy restored (Mica active: {actualMica})");
            foreach (var (width, theme) in new[] { (1080, "light"), (720, "dark"), (1080, "dark"), (620, "light") })
            {
                var scale = root.XamlRoot.RasterizationScale;
                window.AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(660 * scale)));
                AppServices.Settings.ThemeMode = theme;
                AppServices.SaveSettings();
                window.RefreshSettings();
                double? headingTop = null;
                foreach (var route in new[] { "home", "basic", "widgets", "notification", "account", "plugins", "central", "nex", "privacy", "update", "about" })
                {
                    window.NavigateTo(route);
                    await Task.Delay(280);
                    var page = Tree(root).OfType<ScrollViewer>().Single(x => AutomationProperties.GetAutomationId(x) == "Page_" + route && x.IsLoaded);
                    check(window.CurrentPageKey == route && page.ActualWidth > 150 && page.ScrollableWidth <= 1 &&
                        root.ActualTheme == (theme == "dark" ? ElementTheme.Dark : ElementTheme.Light), $"{route}: no horizontal clipping at {width} DIP, {theme}");
                    if (route is "home" or "basic" || route is "notification" && width == 1080)
                    {
                        await Task.Delay(450);
                        var title = Tree(page).OfType<TextBlock>().Single(x => AutomationProperties.GetAutomationId(x) == "SettingsPageTitle");
                        var top = title.TransformToVisual(root).TransformPoint(default).Y;
                        headingTop ??= top;
                        check(Math.Abs(headingTop.Value - top) < 3, $"{route}: settled page heading aligned at {top:0.#} DIP ({width}, {theme})");
                        await Shot($"settings-{route}-{width}-{theme}");
                    }
                }
            }
            window.NavigateTo("overview"); check(window.CurrentPageKey == "home", "Previous overview route opens home");
            window.NavigateTo("系统与启动"); check(window.CurrentPageKey == "basic", "Previous startup route opens general");
        }
        finally
        {
            window.AppWindow.Resize(initial);
            AppServices.Settings.ThemeMode = oldTheme; AppServices.SaveSettings();
            window.NavigateTo("home"); window.RefreshSettings();
        }
    }
}
#endif
