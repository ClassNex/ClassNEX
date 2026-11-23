#if DEBUG
using System.Diagnostics;
using Microsoft.UI.Dispatching;
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
        T Element<T>(DependencyObject parent, string id) where T : FrameworkElement =>
            Tree(parent).OfType<T>().Single(el => AutomationProperties.GetAutomationId(el) == id);
        static Rect Box(FrameworkElement element, UIElement relativeTo) =>
            element.TransformToVisual(relativeTo).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        void ReviewCategories(ScrollViewer page, string key, int expected, int width)
        {
            var list = Element<StackPanel>(page, "Categories_" + key);
            var cards = list.Children.OfType<Button>().ToArray();
            check(cards.Length == expected && !Tree(list).OfType<ToggleSwitch>().Any(), $"{key}: {expected} category links, no controls duplicated on the index ({width} DIP)");
            double previousBottom = -4;
            foreach (var card in cards)
            {
                var id = AutomationProperties.GetAutomationId(card)["Category_".Length..];
                var chevron = Element<FontIcon>(card, "CategoryChevron_" + id);
                var frame = Box(card, list);
                var arrow = Box(chevron, card);
                var inset = frame.Width - arrow.Right;
                check(Shown(card) && card.IsTabStop && frame.Height >= 63 && Math.Abs(frame.Width - list.ActualWidth) <= 1 &&
                    Math.Abs(frame.Top - previousBottom - 4) <= 1.1 && inset is >= 14 and <= 20 && !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(card)),
                    $"{id}: full-width keyboard link; height {frame.Height:0.#}, chevron inset {inset:0.#} DIP ({width} DIP)");
                previousBottom = frame.Bottom;
            }
        }
        void ReviewTrailingEdges(ScrollViewer page, int width, params string[] ids)
        {
            double? rightEdge = null;
            foreach (var id in ids)
            {
                var row = Element<Grid>(page, "Row_" + id);
                var editor = Element<FrameworkElement>(row, id);
                var visibleAction = editor;
                if (editor is ToggleSwitch toggle)
                {
                    var track = Tree(toggle).OfType<FrameworkElement>().Single(el => el.Name == (toggle.IsOn ? "SwitchKnobBounds" : "OuterBorder"));
                    var thumb = Tree(toggle).OfType<FrameworkElement>().Single(el => el.Name == "SwitchKnob");
                    var state = Element<TextBlock>(row, id + "_State");
                    var (text, rail, knob) = (Box(state, row), Box(track, row), Box(thumb, row));
                    var gap = rail.Left - text.Right;
                    var center = knob.X + knob.Width / 2 - (rail.X + rail.Width / 2);
                    check(Shown(track) && Shown(state) && state.Text == (toggle.IsOn ? "开" : "关") && gap is >= 8 and <= 12 &&
                        Math.Abs(rail.Width - 40) <= 1 && (toggle.IsOn ? center > 4 : center < -4),
                        $"{id}: visible {state.Text} left of native {rail.Width:0.#} DIP track; thumb offset {center:0.#} ({width} DIP)");
                    visibleAction = track;
                }
                var inset = row.ActualWidth - Box(visibleAction, row).Right;
                var right = Box(visibleAction, page).Right;
                rightEdge ??= right;
                check(inset is >= 15 and <= 19 && Math.Abs(rightEdge.Value - right) <= 1.1,
                    $"{id}: actual control ends at {right:0.#}, {inset:0.#} DIP from its card edge ({width} DIP)");
            }
        }
        async Task ReviewPanePlacement(ScrollViewer page, int width, string theme, bool takeShot = false)
        {
            var nav = Element<NavigationView>(root, "SettingsNavigation");
            var frame = Element<Frame>(root, "SettingsFrame");
            var titleButton = Tree(root).OfType<Button>().Single(x => x.Name == "PART_PaneToggleButton" && Shown(x));
            var togglePane = new ButtonAutomationPeer(titleButton);
            var row = Element<Grid>(page, "Row_CentralUrl");
            var field = Element<TextBox>(row, "CentralUrl");
            var control = Element<ToggleSwitch>(page, "CentralEnabled");
            var route = frame.Content;
            var selected = nav.SelectedItem;
            var url = field.Text;
            var wide = nav.DisplayMode == NavigationViewDisplayMode.Expanded;
            async Task Verify(bool open, string step)
            {
                await Task.Delay(400);
                var left = wide && open ? nav.OpenPaneLength : nav.CompactPaneLength;
                bool Aligned(Rect r) => Math.Abs(r.Left - left) <= 3 && Math.Abs(r.Right - root.ActualWidth) <= 3;
                var fits = await Until(() => nav.IsPaneOpen == open && Aligned(Box(frame, root)) && Aligned(Box(page, root)));
                var (f, p, r, edit, on) = (Box(frame, root), Box(page, root), Box(row, root), Box(field, root), Box(control, root));
                var body = Box((StackPanel)page.Content, root);
                var middleInset = Math.Max(0, (p.Width - 1080) / 2) + (p.Width < 550 ? 16 : 28);
                check(fits && Math.Abs(body.Left - p.Left) <= 2 && Math.Abs(body.Width - page.ViewportWidth) <= 2 && page.HorizontalOffset <= 1 &&
                    Math.Abs(r.Left - p.Left - middleInset) < 6 && Math.Abs(p.Right - r.Right - middleInset) < 6 &&
                    r.Left <= edit.Left && edit.Right <= r.Right && r.Left <= on.Left && on.Right <= r.Right &&
                    ReferenceEquals(route, frame.Content) && ReferenceEquals(selected, nav.SelectedItem) && field.Text == url,
                    $"Sidebar {step}, {width} DIP {theme}: frame {f.Left:0.#}–{f.Right:0.#}, page {p.Left:0.#}–{p.Right:0.#}, body {body.Left:0.#}–{body.Right:0.#}, viewport {page.ViewportWidth:0.#}, offset {page.HorizontalOffset:0.#}, card {r.Left:0.#}–{r.Right:0.#}, URL {edit.Left:0.#}–{edit.Right:0.#}, switch ends {on.Right:0.#}, client ends {root.ActualWidth:0.#}");
            }
            async Task<string?> Observe(Func<Task> exercise)
            {
                if (!takeShot || !wide) { await exercise(); return null; }

                var timer = root.DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(16);
                timer.IsRepeating = true;
                var gaps = new List<double>();
                long? previous = null;
                double? firstTick = null;
                var pageChanges = 0;
                var rowChanges = 0;
                var started = Stopwatch.GetTimestamp();
                void Tick(DispatcherQueueTimer sender, object args)
                {
                    var now = Stopwatch.GetTimestamp();
                    if (previous is long last) gaps.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                    else firstTick = Stopwatch.GetElapsedTime(started, now).TotalMilliseconds;
                    previous = now;
                }
                void PageChanged(object sender, SizeChangedEventArgs args) { if (args.PreviousSize.Width != args.NewSize.Width) pageChanges++; }
                void RowChanged(object sender, SizeChangedEventArgs args) { if (args.PreviousSize.Width != args.NewSize.Width) rowChanges++; }
                timer.Tick += Tick;
                page.SizeChanged += PageChanged;
                row.SizeChanged += RowChanged;
                try { timer.Start(); await exercise(); }
                finally
                {
                    timer.Stop();
                    timer.Tick -= Tick;
                    page.SizeChanged -= PageChanged;
                    row.SizeChanged -= RowChanged;
                }
                gaps.Sort();
                var count = gaps.Count;
                var first = firstTick is double delay ? $"first tick {delay:0.#} ms" : "no tick";
                var timing = count == 0 ? "0 callback intervals (insufficient samples)" :
                    $"{count} callback intervals, median {(gaps[(count - 1) / 2] + gaps[count / 2]) / 2:0.#} ms, max {gaps[^1]:0.#} ms";
                return $"{first}, {timing}, width changes: page {pageChanges}, card {rowChanges}";
            }
            await Verify(wide, "initial");
            var closeTiming = await Observe(async () =>
            {
                togglePane.Invoke();
                await Verify(!wide, wide ? "closed manually" : "opened as overlay");
            });
            if (takeShot) await Shot($"settings-central-sidebar-collapsed-{width}-{theme}");
            togglePane.Invoke();
            await Verify(wide, "restored");
            var rapidTiming = await Observe(async () =>
            {
                for (var i = 0; i < 5; i++) { togglePane.Invoke(); await Task.Delay(30); }
                await Verify(!wide, "after five rapid presses");
            });
            if (closeTiming != null && rapidTiming != null)
                check(true, $"Sidebar UI dispatcher (16 ms requested; not compositor FPS), {width} DIP {theme}: single collapse: {closeTiming}; five rapid reversals: {rapidTiming}");
            togglePane.Invoke();
            await Verify(wide, "final");
        }
        async Task ReviewEditedCentralUrl()
        {
            var marker = Path.Combine(SettingsService.DataDirectory, "..", ".classnex-review");
            if (!File.Exists(marker) || File.ReadAllText(marker) != "isolated desktop review" ||
                AccountService.Current != null || AppServices.Settings.CentralManagement.IsEnabled)
                throw new InvalidOperationException("Sidebar edit review requires the signed-out, disconnected desktop fixture.");
            var nav = Element<NavigationView>(root, "SettingsNavigation");
            var frame = Element<Frame>(root, "SettingsFrame");
            var field = Element<TextBox>((Page)frame.Content, "CentralUrl");
            var toggle = Tree(root).OfType<Button>().Single(x => x.Name == "PART_PaneToggleButton" && Shown(x));
            var wasOpen = nav.IsPaneOpen;
            var saved = AppServices.Settings.CentralManagement.ServerUrl;
            var edited = $"http://127.0.0.1:1/sidebar-{Guid.NewGuid():N}";
            bool editedWithFocus = false;
            void OnTextChanged(object sender, TextChangedEventArgs args) => editedWithFocus = field.Text == edited && field.FocusState != FocusState.Unfocused;
            field.TextChanged += OnTextChanged;
            try
            {
                window.Activate();
                check(field.Focus(FocusState.Keyboard) && await Until(() => field.FocusState != FocusState.Unfocused), "Sidebar edit: the URL editor receives keyboard focus");
                field.Text = edited;
                check(await Until(() => editedWithFocus), "Sidebar edit: the URL change is delivered while its editor still has focus");
                check(toggle.Focus(FocusState.Keyboard) && await Until(() => field.FocusState == FocusState.Unfocused), "Sidebar edit: moving to the title-bar toggle blurs the unfinished field without Enter");
                new ButtonAutomationPeer(toggle).Invoke();
                check(await Until(() => nav.IsPaneOpen != wasOpen && AppServices.Settings.CentralManagement.ServerUrl == edited) &&
                    SettingsService.Load().CentralManagement.ServerUrl == edited, "Sidebar edit: collapsing commits the loopback URL to the fixture settings file");
                window.NavigateTo("basic");
                window.NavigateTo("central");
                var returned = (Page)frame.Content;
                check(await Until(() => Tree(returned).OfType<TextBox>().Any(x => AutomationProperties.GetAutomationId(x) == "CentralUrl" &&
                    !ReferenceEquals(field, x) && x.IsLoaded && x.Text == edited)), "Sidebar edit: the fresh management page restores the saved URL");
            }
            finally
            {
                field.TextChanged -= OnTextChanged;
                try { window.NavigateTo("basic"); }
                finally
                {
                    AppServices.Settings.CentralManagement.ServerUrl = saved;
                    AppServices.SaveSettings();
                    if (nav.IsPaneOpen != wasOpen) new ButtonAutomationPeer(toggle).Invoke();
                }
            }
        }
        async Task ReviewPaneBreakpointChoice()
        {
            var nav = Element<NavigationView>(root, "SettingsNavigation");
            var toggle = new ButtonAutomationPeer(Tree(root).OfType<Button>().Single(x => x.Name == "PART_PaneToggleButton" && Shown(x)));
            var original = window.AppWindow.Size;
            var scale = root.XamlRoot.RasterizationScale;
            var frameWidth = original.Width / scale - root.ActualWidth;
            async Task ResizeAndCheck(int width, NavigationViewDisplayMode mode, bool open, string reason)
            {
                window.AppWindow.Resize(new SizeInt32((int)Math.Round(width * scale), original.Height));
                var reached = await Until(() => Math.Abs(root.ActualWidth - (width - frameWidth)) <= 2 && nav.DisplayMode == mode);
                await Task.Delay(400);
                check(reached && nav.IsPaneOpen == open, $"Sidebar {reason}: {root.ActualWidth:0.#} DIP, {nav.DisplayMode}, open {nav.IsPaneOpen}");
            }
            check(await Until(() => nav.DisplayMode == NavigationViewDisplayMode.Expanded && nav.IsPaneOpen), "Breakpoint review starts with the full-width rail open");
            toggle.Invoke();
            check(await Until(() => !nav.IsPaneOpen), "User closes the wide rail");
            await ResizeAndCheck(1090, NavigationViewDisplayMode.Expanded, false, "preserves a manual collapse inside the wide range");
            await ResizeAndCheck(720, NavigationViewDisplayMode.Compact, false, "enters icon mode");
            toggle.Invoke();
            check(await Until(() => nav.IsPaneOpen), "User opens the compact overlay");
            window.NavigateTo("central");
            check(await Until(() => !nav.IsPaneOpen), "Choosing a page dismisses the temporary overlay");
            await ResizeAndCheck(1160, NavigationViewDisplayMode.Expanded, false, "restores the manually collapsed wide rail");
            toggle.Invoke();
            check(await Until(() => nav.IsPaneOpen), "User reopens the wide rail");
            await ResizeAndCheck(720, NavigationViewDisplayMode.Compact, false, "closes only the narrow overlay");
            await ResizeAndCheck((int)Math.Round(original.Width / scale), NavigationViewDisplayMode.Expanded, true, "restores the manually opened wide rail");
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
            if (width != 620) return;
            new ButtonAutomationPeer(compact).Invoke();
            check(await Until(() => nav.IsPaneOpen && Shown(search)), "Compact search reopens for an individual setting");
            search.Text = "课前准备提醒";
            new ButtonAutomationPeer(Tree(search).OfType<Button>().Single(b => b.Name == "QueryButton" && Shown(b))).Invoke();
            check(await Until(() => window.CurrentPageKey == "reminder-rules" && Closed() && nav.SelectedItem is NavigationViewItem { Tag: "notification" } &&
                Tree(root).OfType<Expander>().Any(el => AutomationProperties.GetAutomationId(el) == "PrepareDetails" && el.IsExpanded) &&
                FocusManager.GetFocusedElement(root.XamlRoot) is ToggleSwitch focusedToggle && AutomationProperties.GetAutomationId(focusedToggle) == "ClassPrepare"),
                "Searching for a reminder opens its detail page, expands the correct group and focuses its actual switch");
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
            var screenshotScale = root.XamlRoot.RasterizationScale;
            window.AppWindow.Resize(new SizeInt32((int)(1240 * screenshotScale), (int)(720 * screenshotScale)));
            window.NavigateTo("central");
            await Task.Delay(450);
            await ReviewPanePlacement(Element<ScrollViewer>(root, "Page_central"), 1240, AppServices.Settings.ThemeMode, takeShot: true);
            await ReviewEditedCentralUrl();
            await ReviewPaneBreakpointChoice();
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
                foreach (var route in new[] { "home", "basic", "startup", "schedule", "runtime", "widgets", "widget-layout", "appearance", "window",
                    "notification", "reminder-display", "reminder-rules", "reminder-after-school", "account", "plugins", "central", "nex", "nex-assistant", "nex-wake",
                    "privacy", "privacy-storage", "privacy-services", "update", "about", "debug" })
                {
                    window.NavigateTo(route);
                    await Task.Delay(280);
                    var page = Tree(root).OfType<ScrollViewer>().Single(x => AutomationProperties.GetAutomationId(x) == "Page_" + route && x.IsLoaded);
                    check(window.CurrentPageKey == route && page.ActualWidth > 150 && page.ScrollableWidth <= 1 &&
                        root.ActualTheme == (theme == "dark" ? ElementTheme.Dark : ElementTheme.Light), $"{route}: no horizontal clipping at {width} DIP, {theme}");
                    switch (route)
                    {
                        case "basic": case "widgets": ReviewCategories(page, route, 3, width); break;
                        case "nex": case "privacy": ReviewCategories(page, route, 2, width); break;
                        case "notification":
                            ReviewCategories(page, route, 3, width);
                            check(Shown(Element<ToggleSwitch>(page, "Notifications")) && Shown(Element<Button>(page, "PreviewNotification")),
                                $"Notification overview has its master switch and classroom preview ({width} DIP)");
                            break;
                        case "reminder-after-school": ReviewTrailingEdges(page, width, "AfterSchoolEnabled"); break;
                        case "startup": ReviewTrailingEdges(page, width, "RunAtStartup", "StartupSplash", "UrlProtocol"); break;
                        case "runtime": ReviewTrailingEdges(page, width, "TeachingSafeMode", "CrashHandling"); break;
                        case "schedule": ReviewTrailingEdges(page, width, "SemesterStart", "ProfileWeek"); break;
                        case "window": ReviewTrailingEdges(page, width, "Orientation", "Topmost"); break;
                        case "central":
                            ReviewTrailingEdges(page, width, "CentralEnabled", "CentralUrl", "CentralAccount");
                            await ReviewPanePlacement(page, width, theme);
                            break;
                    }
                    if (route is "startup" or "runtime")
                    {
                        await Task.Delay(450);
                        var path = Element<BreadcrumbBar>(page, "SettingsBreadcrumb");
                        check(Shown(path) && !Tree(page).OfType<TextBlock>().Any(t => AutomationProperties.GetAutomationId(t) == "SettingsPageTitle"),
                            $"{route}: breadcrumb replaces duplicate page heading ({width} DIP, {theme})");
                        await Shot($"settings-{route}-{width}-{theme}");
                    }
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
            foreach (var (percent, clientWidth, clientHeight) in new[] { (125, 864, 720), (150, 720, 600) })
            {
                var scale = root.XamlRoot.RasterizationScale;
                bool HasViewport() => Math.Abs(root.ActualWidth - clientWidth) <= 1 && Math.Abs(root.ActualHeight - clientHeight) <= 1 &&
                    Math.Abs(root.XamlRoot.Size.Width - clientWidth) <= 1 && Math.Abs(root.XamlRoot.Size.Height - clientHeight) <= 1;
                // AppWindow's client area can include non-client title bar insets that the XAML tree does not occupy.
                // Measure the live window frame rather than assuming a decoration size at a particular Windows DPI.
                var outer = window.AppWindow.Size;
                window.AppWindow.Resize(new SizeInt32(outer.Width + (int)Math.Round((clientWidth - root.ActualWidth) * scale),
                    outer.Height + (int)Math.Round((clientHeight - root.ActualHeight) * scale)));
                var fits = await Until(HasViewport);
                var actual = root.XamlRoot.Size;
                check(fits, $"{percent}% equivalent viewport for a 1080×900 client: requested {clientWidth}×{clientHeight} DIP, " +
                    $"XamlRoot {actual.Width:0.#}×{actual.Height:0.#}, content {root.ActualWidth:0.#}×{root.ActualHeight:0.#}; actual rasterization remains {scale:P0}");
                window.NavigateTo("basic");
                await Task.Delay(420);
                ReviewCategories(Element<ScrollViewer>(root, "Page_basic"), "basic", 3, clientWidth);
                window.NavigateTo("runtime");
                await Task.Delay(420);
                ReviewTrailingEdges(Element<ScrollViewer>(root, "Page_runtime"), clientWidth, "TeachingSafeMode", "CrashHandling");
            }
            window.NavigateTo("overview"); check(window.CurrentPageKey == "home", "Previous overview route opens home");
            window.NavigateTo("系统与启动"); check(window.CurrentPageKey == "startup", "Previous startup route opens the actual startup controls");
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
