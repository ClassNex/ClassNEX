#if DEBUG
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
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
        static bool Shown(FrameworkElement element)
        {
            if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
            for (DependencyObject? node = element; node != null; node = VisualTreeHelper.GetParent(node))
                if (node is UIElement visual && (visual.Visibility != Visibility.Visible || visual.Opacity == 0)) return false;
            return true;
        }
        static async Task<bool> Until(Func<bool> ready)
        {
            for (var i = 0; i < 60 && !ready(); i++) await Task.Delay(40);
            return ready();
        }
        async Task ReviewSidebarSearch(int width)
        {
            var nav = Tree(root).OfType<NavigationView>().Single();
            var header = (FrameworkElement)nav.PaneCustomContent;
            var search = Tree(header).OfType<AutoSuggestBox>().Single(box => AutomationProperties.GetAutomationId(box) == "SettingsSearch");
            var compact = Tree(header).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "CompactSettingsSearch");
            bool Closed() => !nav.IsPaneOpen && Shown(compact) && !Shown(search);
            if (width >= 840)
            {
                check(await Until(() => nav.IsPaneOpen && Shown(search) && !Shown(compact) &&
                    Tree(header).OfType<AutoSuggestBox>().Count(Shown) == 1 &&
                    !Tree(nav).OfType<Button>().Any(b => b.Name == "PaneAutoSuggestButton" && Shown(b))), $"Wide sidebar shows only the expanded search entry at {width} DIP");
                return;
            }

            check(await Until(Closed), $"Compact sidebar offers the search button at {width} DIP");
            var icon = Tree(compact).OfType<FontIcon>().Single();
            var presenter = Tree(compact).OfType<ContentPresenter>().Single(p => ReferenceEquals(p.Content, icon));
            Rect Bounds(FrameworkElement e) => e.TransformToVisual(compact).TransformBounds(new Rect(0, 0, e.ActualWidth, e.ActualHeight));
            var glyph = Bounds(icon);
            var content = Bounds(presenter);
            var pane = Bounds(header);
            var left = Math.Max(Math.Max(0, pane.Left), content.Left + presenter.Padding.Left + presenter.BorderThickness.Left);
            var right = Math.Min(Math.Min(compact.ActualWidth, pane.Right), content.Right - presenter.Padding.Right - presenter.BorderThickness.Right);
            var center = glyph.X + glyph.Width / 2;
            var pixel = 1 / root.XamlRoot.RasterizationScale;
            check(Shown(icon) && glyph.Width >= 16 - pixel / 2 && right - left >= 16 - .01 && center - 8 >= left - pixel / 2 &&
                center + 8 <= right + pixel / 2 && Math.Abs(center - compact.ActualWidth / 2) <= pixel,
                $"Compact search fits a centered 16 DIP glyph at {width} DIP (glyph {glyph.Width:0.##}, content {left:0.##}–{right:0.##}, center {center:0.##}/{compact.ActualWidth / 2:0.##})");

            new ButtonAutomationPeer(compact).Invoke();
            check(await Until(() => nav.IsPaneOpen && Shown(search) && !Shown(compact) &&
                FocusManager.GetFocusedElement(root.XamlRoot) is DependencyObject focused &&
                Tree(search).OfType<TextBox>().Any(box => Shown(box) && Tree(box).Any(node => ReferenceEquals(node, focused)))),
                $"Invoking compact search expands the sidebar and focuses its text editor at {width} DIP");
            search.Text = "通用";
            var submit = Tree(search).OfType<Button>().Single(b => b.Name == "QueryButton" && Shown(b));
            new ButtonAutomationPeer(submit).Invoke();
            check(await Until(() => window.CurrentPageKey == "basic" && Closed()), $"Submitting the search opens General and restores the compact sidebar at {width} DIP");
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
                window.Activate();
                await ReviewSidebarSearch(width);
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
