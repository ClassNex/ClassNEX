using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClassNex.Models;
using ClassNex.WinUI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI.ViewManagement;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    internal void StartVerificationIfRequested()
    {
        if (Environment.GetCommandLineArgs() is not [_, "--verify-native"]) return;
        var app = new DirectoryInfo(AppContext.BaseDirectory);
        var data = new DirectoryInfo(SettingsService.DataDirectory);
        var marker = Path.Combine(data.FullName, ".native-verify");
        if (app.Parent is not { } root || !root.Name.StartsWith("verify-native-", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(root.Name[14..], "N", out _) || !app.Name.Equals("app", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.Combine(root.FullName, "data"), data.FullName, StringComparison.OrdinalIgnoreCase) || !File.Exists(marker)) return;
        try
        {
            for (DirectoryInfo? dir = app; dir is not null; dir = dir.Parent)
                if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;
            if (data.Attributes.HasFlag(FileAttributes.ReparsePoint) || data.EnumerateFileSystemInfos().Any(x => x.Attributes.HasFlag(FileAttributes.ReparsePoint)) ||
                !string.Equals(File.ReadAllText(marker), "disposable", StringComparison.Ordinal)) return;
            var stream = new FileStream(Path.Combine(data.FullName, "_native_verify.log"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var verification = new NativeVerification(this, new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true });
            _ = Task.Run(verification.RunAsync);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private sealed class NativeVerification(NativeDesktop host, StreamWriter log)
    {
        private const string CourseName = "原生验收课程";
        private const string AccountName = "原生验收账户";
        private static readonly string AccountFile = Path.Combine(SettingsService.DataDirectory, "account.json");
        private readonly string? _expectedVersion = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_PRODUCT_VERSION");
        private string _step = "fixture";
        private bool _ownsAccount;
        private NativeSettingsWindow Settings => host._settings ?? throw new InvalidOperationException("No settings window");
        private NavigationView Navigation => ((Grid)Settings.Content).Children.OfType<NavigationView>().Single();
        private Frame Frame => (Frame)Navigation.Content;
        private Page Page => (Page)Frame.Content;

        internal async Task RunAsync()
        {
            string? failure = null;
            try
            {
                await Check("anonymous-fixture", () => AppServices.Settings is { IsOobeCompleted: true, CentralManagement.IsEnabled: false, IsNexVoiceWakeEnabled: false } s &&
                    Uri.TryCreate(s.CentralManagement.ServerUrl, UriKind.Absolute, out var central) && central.IsLoopback &&
                    Uri.TryCreate(s.AccountEndpoint, UriKind.Absolute, out var auth) && auth.IsLoopback && string.IsNullOrEmpty(s.TimetablePath) &&
                    AccountService.Current is null && !File.Exists(AccountFile) && !File.Exists(Path.Combine(SettingsService.DataDirectory, "account_avatar.png")));
                await VerifyNavigation();
                await VerifySettings();
                await VerifyHomeFeatureCards();
                await VerifyCategoryNavigation();
                await VerifyProductVersion();
                await VerifyOverlay();
                await VerifyDayCache();
                await VerifyEditorBridge();
            }
            catch (Exception ex) { failure = $"{_step} [{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}]"; }
            try
            {
                if (_ownsAccount) await Ui(() => { AccountService.Logout(); return true; });
            }
            catch (Exception ex) { failure ??= $"fixture-cleanup [{ex.GetType().Name}]"; }
            using (log) log.WriteLine(failure is null ? "RESULT PASS" : $"FAIL {failure}{Environment.NewLine}RESULT FAIL");
        }

        private async Task VerifyNavigation()
        {
            var peakHomes = 0;
            Exception? renderError = null;
            void SampleHome(object? sender, object args)
            {
                try { if (host._settings is not null) peakHomes = Math.Max(peakHomes, Tree(Frame).OfType<ScrollViewer>().Count(s => Id(s) == "Page_home" && Shown(s))); }
                catch (Exception ex) { renderError = ex; }
            }
            try
            {
                await Act("open-native-settings", () => { CompositionTarget.Rendering += SampleHome; host.OpenSettings(); });
                await Check("native-home-rendered", () => peakHomes > 0 && Find<TextBlock>(Navigation, "SidebarAccountName") is { Text: "未登录" } && Settings.CurrentPageKey == "home", 8000);
                await Check("exact-eleven-navigation-entries", () => Navigation.MenuItems.OfType<NavigationViewItem>().Select(x => x.Content as string).SequenceEqual(new[] { "主页", "通用", "组件与主界面", "通知服务", "账户", "插件市场", "集控与自动化", "NEX Intelligence与NEX AI", "隐私与安全性", "更新", "关于" }));
                await Act("reopen-home", () => host.OpenSettings());
                await Task.Delay(400).ConfigureAwait(false);
                await Check("one-home-on-first-open", () => renderError is null && peakHomes == 1 && !Frame.CanGoBack && Settings.CurrentPageKey == "home");
            }
            finally { await Ui(() => { CompositionTarget.Rendering -= SampleHome; return true; }); }
            await Snapshot("native-home.png", () => Settings.Content, true);
            await Check("native-navigation-theme", () => Frame.ContentTransitions.Count == 1 && Frame.ContentTransitions[0] is NavigationThemeTransition &&
                Id(Navigation) == "SettingsNavigation" && Id(Frame) == "SettingsFrame");
            NavigationTransitionInfo? transition = null;
            var navigations = 0;
            void OnNav(object sender, NavigationEventArgs args) { transition = args.NavigationTransitionInfo; navigations++; }
            await Act("wire-navigation-probe", () => Frame.Navigated += OnNav);
            try
            {
                await InvokeMeasuredHomeEntry();
                await Check("winui-page-transition", () => Settings.CurrentPageKey == "basic" && navigations == 1 &&
                    (new UISettings().AnimationsEnabled ? transition is EntranceNavigationTransitionInfo : transition is SuppressNavigationTransitionInfo), 5000);
                string[] destinations = ["widgets", "appearance", "notification", "basic", "window", "basic", "appearance"];
                var depth = await Ui(() => Frame.BackStackDepth);
                await Act("repeat-navigation", () => { foreach (var key in destinations) { Settings.NavigateTo(key); Settings.NavigateTo(key); } });
                await Check("rapid-navigation-no-duplicate", () => navigations == 1 + destinations.Length && Frame.BackStackDepth == depth + destinations.Length &&
                    Settings.CurrentPageKey == "appearance" && Navigation.SelectedItem is NavigationViewItem { Tag: "widgets" } &&
                    Tree(Frame).OfType<Page>().Count(Shown) == 1 && Find<ScrollViewer>(Page, "Page_appearance") is { VerticalOffset: 0 } scroller && Shown(scroller), 4000);
                await Check("detail-transition-and-final-focus", () =>
                    (new UISettings().AnimationsEnabled ? transition is DrillInNavigationTransitionInfo : transition is SuppressNavigationTransitionInfo) &&
                    Find<BreadcrumbBar>(Page, "SettingsBreadcrumb") is { } heading && ContainsFocus(heading), 4000);
                await Act("frame-back", () => Frame.GoBack());
                await Check("back-restores-selection", () => Settings.CurrentPageKey == "basic" && Navigation.SelectedItem is NavigationViewItem { Tag: "basic" } && Frame.CanGoForward, 2000);
                await Act("frame-forward", () => Frame.GoForward());
                await Check("forward-restores-page", () => Settings.CurrentPageKey == "appearance" && Find<Slider>(Page, "Opacity") is { } slider && Shown(slider), 3000);
            }
            finally { await Ui(() => { Frame.Navigated -= OnNav; return true; }); }
        }

        private async Task VerifyHomeFeatureCards()
        {
            (string Card, string Route, string Title, string Parent)[] cards =
            [
                ("profile", "schedule", "课表与档案", "basic"), ("widgets", "widgets", "组件与主界面", "widgets"),
                ("notification", "notification", "通知服务", "notification"), ("basic", "basic", "通用", "basic")
            ];
            await Act("open-feature-card-home", () => Settings.NavigateTo("home"));
            await Check("four-default-feature-cards", () => AtPage("home", "主页", "home") && Find<Grid>(Page, "HomeCards") is { } grid &&
                Tree(grid).OfType<Button>().Count(b => Id(b)?.StartsWith("Feature_", StringComparison.Ordinal) == true) == cards.Length &&
                cards.All(c => Find<Button>(grid, "Feature_" + c.Card) is { IsEnabled: true } b && Shown(b)), 4000);
            var homeDepth = await Ui(() => Frame.BackStackDepth);
            foreach (var (card, route, title, parent) in cards)
            {
                await Act("invoke-feature-" + card, () => new ButtonAutomationPeer(Need<Button>(Need<Grid>(Page, "HomeCards"), "Feature_" + card)).Invoke());
                await Check("feature-destination-" + card, () => AtPage(route, title, parent) && Frame.CanGoBack && Frame.BackStackDepth == homeDepth + 1, 4000);
                await Act("back-from-feature-" + card, () => Frame.GoBack());
                await Check("feature-returns-home-" + card, () => AtPage("home", "主页", "home") && Frame.BackStackDepth == homeDepth &&
                    Find<Grid>(Page, "HomeCards") is { } grid && Find<Button>(grid, "Feature_" + card) is { } tile && Shown(tile), 4000);
            }
            await Act("open-profile-deep-link", () => new ButtonAutomationPeer(Need<Button>(Page, "Feature_profile")).Invoke());
            await Check("home-deep-link-opens-detail", () => AtPage("schedule", "课表与档案", "basic") && BreadcrumbParent("通用") is not null, 4000);
            await Act("ascend-from-deep-link", () => new BreadcrumbBarItemAutomationPeer(BreadcrumbParent("通用")!).Invoke());
            await Check("deep-link-breadcrumb-opens-parent", () => AtPage("basic", "通用", "basic") && Frame.BackStackDepth == homeDepth + 1 &&
                Equals(Frame.BackStack.LastOrDefault()?.Parameter, "home") && Find<Button>(Page, "Category_schedule") is { } row && ContainsFocus(row), 4000);
            await Act("back-from-deep-link-parent", () => Frame.GoBack());
            await Check("deep-link-back-skips-child", () => AtPage("home", "主页", "home") && Frame.BackStackDepth == homeDepth, 4000);
        }

        private bool AtPage(string route, string title, string parent)
        {
            if (Settings.CurrentPageKey != route || Navigation.SelectedItem is not NavigationViewItem selected || !Equals(selected.Tag, parent) ||
                Find<ScrollViewer>(Page, "Page_" + route) is not { } body || !Shown(body)) return false;
            if (route == parent) return Find<TextBlock>(body, "SettingsPageTitle") is { } heading && Shown(heading) && heading.Text == title;
            return Find<BreadcrumbBar>(body, "SettingsBreadcrumb") is { } path && Shown(path) && path.ItemsSource is System.Collections.IEnumerable parts &&
                parts.Cast<object>().Select(x => x.ToString()).SequenceEqual(new[] { selected.Content?.ToString(), title });
        }

        private static bool ContainsFocus(FrameworkElement root) => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(root.XamlRoot) is DependencyObject focused &&
            Tree(root).Any(node => ReferenceEquals(node, focused));

        private BreadcrumbBarItem? BreadcrumbParent(string title) => Find<BreadcrumbBar>(Page, "SettingsBreadcrumb") is { } path
            ? Tree(path).OfType<BreadcrumbBarItem>().FirstOrDefault(item => Shown(item) && (Equals(item.Content, title) ||
                Tree(item).OfType<TextBlock>().Any(label => label.Text == title && Shown(label)))) : null;

        private bool SwitchShows(string id, bool on)
        {
            if (Find<ToggleSwitch>(Page, id) is not { IsEnabled: true } toggle || !Shown(toggle) || toggle.IsOn != on ||
                !Equals(toggle.OnContent, "") || !Equals(toggle.OffContent, "") ||
                Find<TextBlock>(Page, id + "_State") is not { } label || !Shown(label) || label.Text != (on ? "开" : "关") ||
                VisualTreeHelper.GetParent(toggle) is not StackPanel { Orientation: Orientation.Horizontal } group ||
                !ReferenceEquals(VisualTreeHelper.GetParent(label), group)) return false;
            var left = label.TransformToVisual(group).TransformPoint(new Windows.Foundation.Point()).X;
            var right = toggle.TransformToVisual(group).TransformPoint(new Windows.Foundation.Point()).X;
            return left + label.ActualWidth <= right + 1;
        }

        private async Task VerifyCategoryNavigation()
        {
            (string Key, string Title, (string Key, string Title)[] Details)[] categories =
            [
                ("basic", "通用", [("startup", "启动与托盘"), ("schedule", "课表与档案"), ("runtime", "运行与恢复")]),
                ("widgets", "组件与主界面", [("widget-layout", "组件布局"), ("appearance", "外观与主题"), ("window", "窗口行为")]),
                ("notification", "通知服务", [("reminder-rules", "上下课提醒"), ("reminder-after-school", "放学提醒"), ("reminder-display", "提醒方式")]),
                ("nex", "NEX Intelligence与NEX AI", [("nex-assistant", "课堂助手"), ("nex-wake", "唤醒方式")]),
                ("privacy", "隐私与安全性", [("privacy-storage", "本机数据"), ("privacy-services", "联网与权限")])
            ];
            foreach (var (parent, parentTitle, details) in categories)
            {
                await Act("open-category-index-" + parent, () => Settings.NavigateTo(parent));
                await Check("category-list-" + parent, () => AtPage(parent, parentTitle, parent) &&
                    Find<StackPanel>(Page, "Categories_" + parent) is { } list && list.Children.OfType<Button>().Select(Id).SequenceEqual(details.Select(d => "Category_" + d.Key)) &&
                    list.Children.OfType<Button>().All(row => Shown(row) && row.IsEnabled && Math.Abs(list.ActualWidth - row.ActualWidth) <= 2) &&
                    (parent == "notification" ? Find<ToggleSwitch>(Page, "Notifications") is not null && Find<ComboBox>(Page, "PreviewKind")?.Items.Count == 4 &&
                        Find<Button>(Page, "PreviewNotification") is { } preview && preview.IsEnabled == (AppServices.Settings.AllowNotification && AppServices.Settings.IsMainWindowVisible)
                        : !Tree(Page).OfType<ToggleSwitch>().Any()), 5000);
                var depth = await Ui(() => Frame.BackStackDepth);
                foreach (var (child, childTitle) in details)
                {
                    void OpenDetail() => new ButtonAutomationPeer(Need<Button>(Page, "Category_" + child)).Invoke();
                    await Act("invoke-option-" + child, OpenDetail);
                    await Check("option-opens-" + child, () => AtPage(child, childTitle, parent) && Frame.BackStackDepth == depth + 1 && BreadcrumbParent(parentTitle) is not null, 5000);
                    if (child == "startup")
                    {
                        var saved = SettingsService.Load();
                        await Check("general-shows-saved-startup-values", () => SwitchShows("RunAtStartup", saved.RunAtStartup) && SwitchShows("StartupSplash", saved.ShowStartupSplash) && SwitchShows("UrlProtocol", saved.RegisterUrlProtocol), 4000);
                    }
                    if (child == "runtime")
                    {
                        await Check("native-toggle-switch", () => SwitchShows("TeachingSafeMode", false), 4000);
                        await Act("switch-native-on", () => new ToggleSwitchAutomationPeer(Need<ToggleSwitch>(Page, "TeachingSafeMode")).Toggle());
                        await Check("toggle-persisted", () => SwitchShows("TeachingSafeMode", true) && AppServices.Settings.TeachingSafeMode && SettingsService.Load().TeachingSafeMode, 3000);
                    }
                    if (child == "reminder-after-school")
                    {
                        await Check("dismissal-opt-in-default", () => SwitchShows("AfterSchoolEnabled", false) && Find<Button>(Page, "PreviewAfterSchool") is not null);
                        await Act("enable-dismissal", () => new ToggleSwitchAutomationPeer(Need<ToggleSwitch>(Page, "AfterSchoolEnabled")).Toggle());
                        await Check("dismissal-switch-persisted", () => SwitchShows("AfterSchoolEnabled", true) && SettingsService.Load().Notification.IsAfterSchoolNotificationEnabled, 3000);
                        await Act("focus-dismissal-message", () => Need<TextBox>(Page, "AfterSchoolText").Focus(FocusState.Keyboard));
                        await Check("dismissal-message-has-focus", () => ContainsFocus(Need<TextBox>(Page, "AfterSchoolText")), 3000);
                    }
                    await Act("invoke-parent-crumb-" + child, () =>
                    {
                        if (child == "reminder-after-school")
                        {
                            var field = Need<TextBox>(Page, "AfterSchoolText");
                            field.SelectAll();
                            field.SelectedText = "明天见。";
                            Settings.RefreshSettings();
                            if (field.Text != "明天见。") throw new InvalidOperationException("Settings refresh overwrote the uncommitted dismissal message");
                        }
                        new BreadcrumbBarItemAutomationPeer(BreadcrumbParent(parentTitle) ?? throw new InvalidOperationException("Missing parent breadcrumb")).Invoke();
                    });
                    await Check("breadcrumb-returns-" + child, () => AtPage(parent, parentTitle, parent) && Frame.BackStackDepth == depth &&
                        Find<Button>(Page, "Category_" + child) is { } row && ContainsFocus(row), 5000);
                    if (child == "reminder-after-school")
                        await Check("dismissal-saved-through-navigation", () => AppServices.Settings.Notification.AfterSchoolText == "明天见。" &&
                            SettingsService.Load().Notification is { IsAfterSchoolNotificationEnabled: true, AfterSchoolText: "明天见。" });
                    if (child != "runtime") continue;
                    await Act("reopen-runtime-option", OpenDetail);
                    await Check("switch-state-survives-reentry", () => AtPage(child, childTitle, parent) && SwitchShows("TeachingSafeMode", true), 4000);
                    await Act("switch-native-off", () => new ToggleSwitchAutomationPeer(Need<ToggleSwitch>(Page, "TeachingSafeMode")).Toggle());
                    await Check("switch-off-state-and-disk-agree", () => SwitchShows("TeachingSafeMode", false) && !AppServices.Settings.TeachingSafeMode && !SettingsService.Load().TeachingSafeMode, 3000);
                    await Act("return-from-runtime-recheck", () => Frame.GoBack());
                    await Check("runtime-recheck-restores-index", () => AtPage(parent, parentTitle, parent) && Frame.BackStackDepth == depth, 4000);
                }
            }
        }

        private async Task VerifyProductVersion()
        {
            await Check("exact-desktop-product-version", () => !string.IsNullOrEmpty(_expectedVersion) && AppServices.ProductVersion == _expectedVersion &&
                ReadStatus(NativeSettingsKeys.Version) == _expectedVersion && new AppServicesAiContext(() => AppServices.Settings, AppServices.Schedule, AppServices.Time, SettingsService.DataDirectory).Version == _expectedVersion &&
                FileVersionInfo.GetVersionInfo(Path.Combine(AppContext.BaseDirectory, "ClassNex.exe")).ProductVersion == _expectedVersion &&
                FileVersionInfo.GetVersionInfo(typeof(AppServices).Assembly.Location).ProductVersion == _expectedVersion);
            await Act("open-about-version", () => Settings.NavigateTo("about"));
            await Check("about-shows-exact-product-version", () => Find<Button>(Page, "Version") is { } version && Shown(version) && Equals(version.Content, _expectedVersion), 5000);
            await Act("open-update-from-about", () => new ButtonAutomationPeer(Need<Button>(Page, "AboutUpdate")).Invoke());
            await Check("update-shows-exact-product-version", () => Settings.CurrentPageKey == "update" && Find<TextBlock>(Page, "InstalledVersion") is { } value && value.Text == _expectedVersion && Shown(value), 4000);
        }

        private async Task InvokeMeasuredHomeEntry()
        {
            var frames = 0;
            TimeSpan? lastRendering = null;
            void FrameDrawn(object? _, object args)
            {
                if (args is RenderingEventArgs tick && lastRendering == tick.RenderingTime) return;
                lastRendering = (args as RenderingEventArgs)?.RenderingTime;
                frames++;
            }
            using var process = Process.GetCurrentProcess();
            long start = 0, bytes = 0;
            var cpu = TimeSpan.Zero;
            try
            {
                await Act("invoke-home-entry", () =>
                {
                    var peer = new ButtonAutomationPeer(Need<Button>(Page, "Home_basic"));
                    CompositionTarget.Rendering += FrameDrawn;
                    process.Refresh();
                    cpu = process.TotalProcessorTime;
                    bytes = GC.GetAllocatedBytesForCurrentThread();
                    start = Stopwatch.GetTimestamp();
                    peer.Invoke();
                });
                await Task.Delay(500).ConfigureAwait(false);
            }
            finally { await Ui(() => { CompositionTarget.Rendering -= FrameDrawn; return true; }); }
            var sample = await Ui(() =>
            {
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                process.Refresh();
                return new { route = "home->basic", animationsEnabled = new UISettings().AnimationsEnabled, observedMs = elapsed,
                    xamlRenderingCallbacks = frames, processCpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, uiThreadManagedAllocatedBytes = allocated };
            });
            try
            {
                using var file = new FileStream(Path.Combine(SettingsService.DataDirectory, "_native_navigation_metrics.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                await JsonSerializer.SerializeAsync(file, sample);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        private async Task VerifySettings()
        {
            var size = await Ui(() => Settings.AppWindow.Size);
            try
            {
                await Act("narrow-settings", () => Settings.AppWindow.Resize(new SizeInt32((int)Math.Round(630 * Page.XamlRoot.RasterizationScale), size.Height)));
                await Check("appearance-slider-fits-narrow-window", () =>
                {
                    var scroll = Need<ScrollViewer>(Page, "Page_appearance");
                    var slider = Need<Slider>(Page, "Opacity");
                    var x = slider.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
                    return Page.XamlRoot.Size.Width <= 635 && Shown(slider) && x >= -1 && x + slider.ActualWidth <= scroll.ActualWidth + 1 && scroll.ScrollableWidth <= 1;
                }, 4000);
            }
            finally { await Ui(() => { Settings.AppWindow.Resize(size); return true; }); }
            await Act("edit-native-appearance", () => { Need<ComboBox>(Page, "Theme").SelectedIndex = 1; Need<Slider>(Page, "Opacity").Value = .65; });
            await Check("slider-and-light-theme-persisted", () => SettingsService.Load() is { ThemeMode: "light" } s && Math.Abs(s.BackgroundOpacity - .65) < .001 &&
                ((FrameworkElement)Settings.Content).ActualTheme == ElementTheme.Light, 4000);
            var account = new AccountProfile { Id = "fixture/local", Name = "native-fixture", DisplayName = AccountName, Email = "native@example.invalid" };
            await Act("load-disposable-identity", () =>
            {
                using (var file = new FileStream(AccountFile, FileMode.CreateNew, FileAccess.Write)) { _ownsAccount = true; JsonSerializer.Serialize(file, account); }
                AccountService.Load();
            });
            await Check("fixture-account-loaded", () => AccountService.Current is { IsLoggedIn: true, DisplayName: AccountName, AccessToken.Length: 0 });
            var local = await Ui(() => AppServices.Settings);
            var session = await Ui(() => AccountService.Current);
            await Act("edit-while-signed-in", () => Need<Slider>(Page, "Opacity").Value = .70);
            await Check("local-edit-preserves-session", () => ReferenceEquals(session, AccountService.Current) && Math.Abs(SettingsService.Load().BackgroundOpacity - .70) < .001, 3000);
            await Act("apply-managed-fixture", () =>
            {
                var remote = SettingsService.Load();
                remote.ThemeMode = "dark";
                remote.BackgroundOpacity = .75;
                remote.UserName = "remote-fixture";
                remote.Email = "remote@example.invalid";
                remote.AccountEndpoint = "http://127.0.0.1:2";
                remote.AccountClientId = "remote-fixture";
                remote.IsOobeCompleted = false;
                remote.CentralManagement = new() { IsEnabled = true, ServerUrl = "http://127.0.0.1:2" };
                remote.IsMainWindowVisible = true;
                AppServices.ApplyManagedSettings(remote, TestTimetable());
            });
            await Check("managed-theme-updates-live-controls", () => ((FrameworkElement)Settings.Content).ActualTheme == ElementTheme.Dark &&
                Need<ComboBox>(Page, "Theme").SelectedIndex == 2 && Math.Abs(Need<Slider>(Page, "Opacity").Value - .75) < .001 && SettingsService.Load().ThemeMode == "dark", 4000);
            await Check("managed-settings-preserve-local-account", () => AppServices.Settings is { IsOobeCompleted: true, CentralManagement.IsEnabled: false } current &&
                current.AccountEndpoint == local.AccountEndpoint && current.AccountClientId == local.AccountClientId && current.UserName == local.UserName && current.Email == local.Email &&
                ReferenceEquals(local.CentralManagement, current.CentralManagement) && session is { IsLoggedIn: true } && ReferenceEquals(session, AccountService.Current) &&
                JsonSerializer.Deserialize<AccountProfile>(File.ReadAllText(AccountFile)) is { DisplayName: AccountName, AccessToken.Length: 0 });
            await Act("open-account", () => Settings.NavigateTo("account"));
            await Check("native-account-shows-same-user", () => Find<TextBlock>(Page, "AccountName") is { Text: AccountName } &&
                Find<TextBlock>(Page, "AccountEmail")?.Text == account.Email && Find<Button>(Page, "AccountAction")?.Content as string == "退出登录", 3000);
        }

        private async Task VerifyOverlay()
        {
            var overlay = host._overlay;
            T Element<T>(string name) where T : Avalonia.Controls.Control => Avalonia.Controls.ControlExtensions.FindControl<T>(overlay, name)!;
            var track = await Ui(() => Element<Avalonia.Controls.Panel>("NotificationTrack"));
            var island = await Ui(() => Element<Avalonia.Controls.Border>("IslandBackground"));
            await Check("managed-timetable-rendered", () => overlay.IsVisible && host.ReadOverlay().Today.Current?.Subject == CourseName && island.Bounds.Width > 0, 5000);
            await Act("open-notification-settings", () => Settings.NavigateTo("reminder-rules"));
            await Check("reminders-initially-collapsed", () => Tree(Page).OfType<Expander>().Count() >= 3 && Tree(Page).OfType<Expander>().All(x => !x.IsExpanded), 3500);
            await Act("expand-details-only", () => Tree(Page).OfType<Expander>().First().IsExpanded = true);
            await Act("refresh-reminder-settings", Settings.RefreshSettings);
            await Check("refresh-preserves-disclosure", () => Tree(Page).OfType<Expander>().First().IsExpanded && !AppServices.Settings.Notification.IsClassOnPreparingNotificationEnabled);
            await Act("reenter-notifications", () => { Settings.NavigateTo("home"); Settings.NavigateTo("reminder-rules"); });
            await Check("reentry-collapses-details", () => Tree(Page).OfType<Expander>().Count() >= 3 && Tree(Page).OfType<Expander>().All(x => !x.IsExpanded), 3000);
            await Act("schedule-enabled-reminder", () =>
            {
                var s = AppServices.Settings;
                s.AllowNotification = true; s.AllowMinorNotification = true; s.AllowNotificationEffect = false;
                s.Notification.IsClassOnPreparingNotificationEnabled = true;
                var start = DateTime.Now.AddSeconds(11);
                if (start.Date != DateTime.Today) throw new InvalidOperationException("Fixture crosses midnight; retry after midnight");
                var profile = CsesCodec.Parse(TestTimetable());
                foreach (var schedule in profile.Schedules)
                {
                    var course = schedule.Classes.Single(); course.StartTime = start.ToString("HH:mm:ss");
                    course.EndTime = (start.Hour == 23 ? start.Date.AddDays(1).AddSeconds(-1) : start.AddMinutes(35)).ToString("HH:mm:ss");
                }
                AppServices.ApplyManagedSettings(s, CsesCodec.Serialize(profile));
            });
            await Check("scheduled-reminder-while-collapsed", () => track.IsVisible && Tree(Page).OfType<Expander>().All(x => !x.IsExpanded), 6000);
            await Snapshot("native-reminders-collapsed.png", () => Settings.Content, true);
            await Check("full-next-subject-on-classroom", () => Element<Avalonia.Controls.TextBlock>("NotificationOverlaySubject") is { Text: CourseName, IsEffectivelyVisible: true }, 6000);
            await Check("notification-restores-course-and-bounds", () => !track.IsVisible && Element<Avalonia.Controls.Border>("CardContent") is { IsEffectivelyVisible: true } card && Math.Abs(island.Bounds.Width - Math.Ceiling(card.Bounds.Width + card.Margin.Left * 2)) <= 2, 16000);
            await Act("restore-all-day-fixture", () => AppServices.ApplyManagedSettings(AppServices.Settings, TestTimetable()));
        }

        private async Task VerifyDayCache()
        {
            await Check("overlay-ticks-reuse-course-list", () =>
            {
                var previous = host.ReadOverlay();
                for (var i = 0; i < 20; i++)
                {
                    var tick = host.ReadOverlay();
                    if (tick.Now.Date == previous.Now.Date && !ReferenceEquals(tick.Today.Slots, previous.Today.Slots)) return false;
                    previous = tick;
                }
                return true;
            });
            var monday = new DateTime(2024, 1, 1, 9, 0, 0);
            var options = new AppSettings { SingleWeekStartTime = monday.Date, WeekRotationOffset = 0 };
            var schedules = new ScheduleService(() => options);
            schedules.Load(SettingsService.TimetableStoragePath);
            schedules.Profile.Schedules.Clear();
            void Slot(int day, string parity, string subject, string start, string end) => schedules.Profile.GetOrCreateSchedule(day, parity).Classes.Add(
                new() { Subject = subject, StartTime = start, EndTime = end });
            Slot(1, "odd", "单周甲", "09:00", "09:45");
            Slot(1, "odd", "单周乙", "10:00", "10:45");
            Slot(1, "even", "双周甲", "09:00", "09:45");
            Slot(2, "all", "周二甲", "09:00", "09:45");
            using var cache = new DayScheduleCache(new TimeService(() => options), schedules);
            var first = cache.Read(monday);
            await Check("day-cache-ordinary-seconds-and-course-boundary", () =>
            {
                for (var i = 1; i <= 60; i++)
                {
                    var tick = cache.Read(monday.AddSeconds(i));
                    if (!ReferenceEquals(tick.Slots, first.Slots) || tick.Current?.Subject != "单周甲" || tick.Next?.Subject != "单周乙") return false;
                }
                var between = cache.Read(monday.AddMinutes(45));
                var next = cache.Read(monday.AddHours(1));
                return ReferenceEquals(between.Slots, first.Slots) && between.Current is null && between.Next?.Subject == "单周乙" &&
                    ReferenceEquals(next.Slots, first.Slots) && next.Current?.Subject == "单周乙" && next.Next is null;
            });
            await Check("day-cache-midnight-invalidates", () =>
            {
                var late = cache.Read(monday.Date.AddDays(1).AddTicks(-1));
                var midnight = cache.Read(monday.Date.AddDays(1));
                return late.Parity == midnight.Parity && midnight.DayNumber == 2 && midnight.Next?.Subject == "周二甲" && !ReferenceEquals(late.Slots, midnight.Slots);
            });
            var afterParity = await Ui(() => cache.Read(monday));
            await Check("day-cache-parity-settings-invalidate", () =>
            {
                options.WeekRotationOffset = 1;
                var even = cache.Read(monday);
                options.SingleWeekStartTime = monday.Date.AddDays(-7);
                var odd = cache.Read(monday);
                var passed = !ReferenceEquals(afterParity.Slots, even.Slots) && even is { Parity: "even", Current.Subject: "双周甲" } &&
                    !ReferenceEquals(even.Slots, odd.Slots) && odd is { Parity: "odd", Current.Subject: "单周甲" };
                afterParity = odd;
                return passed;
            });
            await Check("day-cache-reload-invalidates", () =>
            {
                schedules.Reload();
                var reloaded = cache.Read(monday);
                return reloaded.DayNumber == afterParity.DayNumber && reloaded.Parity == afterParity.Parity && reloaded.Current?.Subject == CourseName &&
                    !ReferenceEquals(reloaded.Slots, afterParity.Slots);
            });
        }

        private async Task VerifyEditorBridge()
        {
            _step = "open-auxiliary-profile-editor";
            var path = SettingsService.TimetableStoragePath;
            var original = await File.ReadAllBytesAsync(path);
            await Act("open-schedule-settings", () => Settings.NavigateTo("schedule"));
            await Check("profile-management-entry-visible", () => Find<Button>(Page, "ProfileInfo") is { } button && Shown(button), 4000);
            await Act("invoke-profile-information", () => new ButtonAutomationPeer(Need<Button>(Page, "ProfileInfo")).Invoke());
            await Check("profile-information-opened-from-settings", () =>
                Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d &&
                d.Windows.OfType<Views.ProfileEditorWindow>().SingleOrDefault() is { IsVisible: true } w &&
                Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.ScrollViewer>(w, "PageProfile") is { IsVisible: true }, 5000);
            var (window, page, editor) = await Ui(() =>
            {
                var lifetime = Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
                    ?? throw new InvalidOperationException("Missing auxiliary desktop lifetime");
                var settings = Settings;
                var key = settings.CurrentPageKey;
                return (settings, key, lifetime.Windows.OfType<Views.ProfileEditorWindow>().Single());
            });
            nint editorHwnd = 0;
            try
            {
                await Check("profile-editor-exact-product-version", () =>
                    Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.TextBlock>(editor, "ProfileVersionText") is { IsEffectivelyVisible: true } version && version.Text == _expectedVersion, 4000);
                await Check("bridge-profile-editor-shares-visible-sta", () =>
                {
                    editorHwnd = editor.TryGetPlatformHandle()?.Handle ?? 0;
                    var nativeHwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    if (!editor.IsVisible || editorHwnd == 0 || editorHwnd == nativeHwnd || !IsWindowVisible(editorHwnd)) return false;
                    var editorThread = GetWindowThreadProcessId(editorHwnd, out var editorProcess);
                    return editorThread != 0 && editorThread == GetWindowThreadProcessId(nativeHwnd, out var settingsProcess) &&
                        editorProcess == (uint)Environment.ProcessId && settingsProcess == editorProcess && Avalonia.Threading.Dispatcher.UIThread.CheckAccess() &&
                        Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;
                }, 5000);
            }
            finally { await Ui(() => { editor.Close(); window.Activate(); return true; }); }
            var after = await File.ReadAllBytesAsync(path);
            var unchanged = original.AsSpan().SequenceEqual(after);
            await Check("bridge-close-restores-settings-without-edit", () => unchanged && !editor.IsVisible && !IsWindowVisible(editorHwnd) &&
                ReferenceEquals(Settings, window) && window.AppWindow.IsVisible && Settings.CurrentPageKey == page && Page.IsLoaded, 4000);
        }

        private static string TestTimetable() => CsesCodec.Serialize(new ScheduleProfile
        {
            Subjects = [new() { Name = CourseName, SimplifiedName = CourseName }],
            Schedules = Enumerable.Range(1, 7).Select(day => new Schedule
            {
                Name = "fixture", EnableDay = day, Weeks = "all",
                Classes = [new() { Subject = CourseName, StartTime = "00:00:00", EndTime = "23:59:59.9999999" }]
            }).ToList()
        });

        internal async Task Snapshot(string fileName, Func<UIElement> root, bool opaque = false)
        {
            try
            {
                var bitmap = await Ui(() => new RenderTargetBitmap());
                var timeout = TimeSpan.FromSeconds(4);
                await Ui(() => bitmap.RenderAsync(root()).AsTask()).Unwrap().WaitAsync(timeout);
                var buffer = await Ui(() => bitmap.GetPixelsAsync().AsTask()).Unwrap().WaitAsync(timeout);
                var (width, height) = await Ui(() => (checked((uint)bitmap.PixelWidth), checked((uint)bitmap.PixelHeight)));
                if (width == 0 || height == 0 || buffer.Length != checked(width * height * 4)) throw new InvalidDataException("Invalid pixel buffer");
                var pixels = new byte[buffer.Length];
                using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(pixels);
                if (opaque) Flatten(pixels, await Ui(() => CaptureBase((FrameworkElement)root())));
                using var png = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, png);
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, width, height, 96, 96, pixels);
                await encoder.FlushAsync();
                png.Seek(0);
                using var source = png.AsStreamForRead();
                using var output = new FileStream(Path.Combine(SettingsService.DataDirectory, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(output);
            }
            catch (Exception ex)
            {
                try { await File.AppendAllTextAsync(Path.Combine(SettingsService.DataDirectory, "_native_capture.log"), $"{fileName} [{ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}]\n"); }
                catch (Exception io) when (io is IOException or UnauthorizedAccessException) { }
            }
        }

        private static Windows.UI.Color CaptureBase(FrameworkElement root)
        {
            if (new AccessibilitySettings().HighContrast) return new UISettings().GetColorValue(UIColorType.Background);
            var key = root.ActualTheme == ElementTheme.Light ? "Light" : "Default";
            var resources = Application.Current.Resources;
            foreach (var source in resources.MergedDictionaries.Prepend(resources))
                if (source.ThemeDictionaries.TryGetValue(key, out var theme) && theme is ResourceDictionary dictionary &&
                    dictionary.TryGetValue("SolidBackgroundFillColorBase", out var value) && value is Windows.UI.Color resolved) return resolved;
            var channel = key == "Light" ? (byte)0xf3 : (byte)0x20;
            return Windows.UI.Color.FromArgb(255, channel, channel, channel);
        }

        private static void Flatten(byte[] pixels, Windows.UI.Color backdrop)
        {
            static byte Blend(byte foreground, byte background, int remaining) => (byte)Math.Min(255, foreground + (background * remaining + 127) / 255);
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var remaining = 255 - pixels[i + 3];
                if (remaining == 0) continue;
                pixels[i] = Blend(pixels[i], backdrop.B, remaining);
                pixels[i + 1] = Blend(pixels[i + 1], backdrop.G, remaining);
                pixels[i + 2] = Blend(pixels[i + 2], backdrop.R, remaining);
                pixels[i + 3] = 255;
            }
        }

        private async Task Act(string key, Action operation)
        {
            _step = key;
            await Ui(() => { operation(); return true; }).ConfigureAwait(false);
        }

        private async Task Check(string key, Func<bool> probe, int timeout = 0, Func<string>? details = null)
        {
            _step = key;
            var timer = Stopwatch.StartNew();
            while (!await Ui(probe).ConfigureAwait(false))
            {
                if (timer.ElapsedMilliseconds >= timeout) throw new InvalidOperationException(details is null ? "condition not met" : await Ui(details).ConfigureAwait(false));
                await Task.Delay(40).ConfigureAwait(false);
            }
            log.WriteLine("PASS " + key);
        }

        private Task<T> Ui<T>(Func<T> action)
        {
            var pending = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!host._queue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                try { pending.TrySetResult(action()); } catch (Exception ex) { pending.TrySetException(ex); }
            })) pending.TrySetException(new InvalidOperationException("WinUI dispatcher stopped"));
            return pending.Task;
        }

        private static IEnumerable<DependencyObject> Tree(DependencyObject root)
        {
            var nodes = new Stack<DependencyObject>();
            nodes.Push(root);
            while (nodes.TryPop(out var node))
            {
                yield return node;
                for (var i = VisualTreeHelper.GetChildrenCount(node) - 1; i >= 0; i--) nodes.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        private static bool Shown(FrameworkElement element)
        {
            if (element is not { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 }) return false;
            for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is UIElement e && (e.Visibility != Visibility.Visible || e.Opacity < .001)) return false;
            return true;
        }
        private static string Id(DependencyObject obj) => AutomationProperties.GetAutomationId(obj);
        private static T? Find<T>(DependencyObject root, string id) where T : FrameworkElement => Tree(root).OfType<T>().FirstOrDefault(e => Id(e) == id);
        private static T Need<T>(DependencyObject root, string id) where T : FrameworkElement => Find<T>(root, id) ?? throw new InvalidOperationException("Missing control: " + id);
        private static bool VisibleText(DependencyObject root, string value) => Tree(root).OfType<TextBlock>().Any(t => t.Text?.Contains(value, StringComparison.Ordinal) == true && Shown(t));

        [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowLongW(nint hwnd, int index);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll", ExactSpelling = true)] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
        [DllImport("user32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint hwnd);
    }
}
