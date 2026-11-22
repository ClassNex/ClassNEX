#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaFluentUI.Controls;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private sealed record Capture(string Surface, string Theme, double Scaling, int Width, int Height, int VisibleText);

    private async Task ReviewWindowsAsync(string directory, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var captures = new List<Capture>();
        var checks = new List<string>();
        var ticket = Stopwatch.StartNew();
        var exitCode = 0;
        Directory.CreateDirectory(directory);
        void Check(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        try
        {
            Check(AccountService.Current is null && !AppServices.Settings.CentralManagement.IsEnabled,
                "Anonymous fixture, no production account or management connection");
            Check(AppServices.Settings.SampleSeedVersion == 3 && Math.Abs(AppServices.Settings.BackgroundOpacity - .55) < .001 &&
                AppServices.Schedule.Profile.Schedules.Any(s => s.Name == "教学日"), "Alpha settings and edited portable timetable survive first startup");
            await ReviewVoiceLifecycleAsync(Check);
            var overlay = _mainWindow ?? throw new InvalidOperationException("No classroom window");
            await Task.Delay(500);
            OpenSettings();
            var settings = _settingsWindow ?? throw new InvalidOperationException("No settings window");
            settings.Width = 990;
            settings.Height = 660;
            OpenProfileEditor();
            var editor = _profileEditor ?? throw new InvalidOperationException("No schedule editor");
            editor.Width = 1020;
            editor.Height = 680;
            OpenLoginWindow();
            var login = _loginWindow ?? throw new InvalidOperationException("No sign-in window");
            Check(!AccountService.FlowActive, "Opening sign-in does not start browser authorization");
            var states = new (string Surface, string Page)[] { ("overview","home"), ("widgets","widgets"),
                ("notifications","notification"), ("plugins","plugins"), ("appearance","appearance"),
                ("account","account"), ("nex","nex"), ("central","central"), ("schedule-settings","schedule"),
                ("profile-settings","profile"), ("system","basic"), ("overlay-settings","window"), ("developer","debug"), ("about","about") };
            var nav = editor.FindControl<NavigationView>("NavView") ?? throw new InvalidOperationException("Profile navigation missing");
            var windows = new Window[] { settings, editor, login, overlay };
            foreach (var dpi in new[] { 96, 120, 144 })
            {
                foreach (var window in windows) SetReviewDpi(window, dpi);
                await Task.Delay(180);
                foreach (var window in windows)
                    Check(Math.Abs(window.RenderScaling - dpi / 96d) < 0.001, $"{window.Title}: backend accepted {dpi} dpi");
                foreach (var mode in new[] { "light", "dark" })
                {
                    ApplyTheme(mode);
                    await Task.Delay(160);
                    Check(settings.ActualThemeVariant == (mode == "light" ? ThemeVariant.Light : ThemeVariant.Dark), $"Theme {mode} at {dpi} dpi");
                    await Shot(overlay, "classroom", mode, dpi);
                    foreach (var (surface, page) in states)
                    {
                        settings.NavigateTo(page);
                        await Shot(settings, surface, mode, dpi);
                        Check(settings.CurrentPageKey == page, $"Navigation reached {page}: {mode} at {dpi} dpi");
                    }
                    editor.SelectTab(0);
                    await Shot(editor, "schedule", mode, dpi);
                    nav.SelectedItem = nav.MenuItems[0];
                    await Shot(editor, "profile", mode, dpi);
                    await Shot(login, "login", mode, dpi);
                }
                if (dpi != 144) await CheckMaskScaling(dpi);
            }
            var before = Stopwatch.GetTimestamp();
            for (var i = 0; i < 30; i++) settings.NavigateTo(i % 2 == 0 ? "home" : "appearance");
            checks.Add($"30 warm navigation requests: {Stopwatch.GetElapsedTime(before).TotalMilliseconds:F0} ms on UI thread (does not measure frame rate)");
            settings.Width = 720;
            settings.Height = 570;
            settings.NavigateTo("home");
            await Shot(settings, "overview-compact", "dark", 144);
            await ReviewRemindersAsync(overlay, Shot, Check);
            ApplyTheme("system");
            Check(FluentTheme?.PreferSystemTheme == true, "System theme tracking restored");
            Check(!overlay.Topmost, "Overlay honors the disabled always-on-top setting");
            var wakeBar = new NexEdgeBarWindow();
            try
            {
                var wakeCount = 0;
                wakeBar.WakeRequested += () => wakeCount++;
                wakeBar.Show();
                await Task.Delay(150);
                var wake = wakeBar.FindControl<Button>("WakeButton") ?? throw new InvalidOperationException("Edge bar wake action missing");
                wake.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Check(wakeCount == 1 && !wakeBar.ShowInTaskbar, "Edge bar exposes one wake action and stays out of the taskbar");
            }
            finally { wakeBar.Close(); }
            login.Close(); editor.Close(); settings.Close();
            Check(_loginWindow is null && _settingsWindow is null && _profileEditor is null, "Window references released on close");
            await CheckDesktopControls();
            async Task CheckMaskScaling(int dpi)
            {
                var card = overlay.FindControl<Border>("CardContent")!;
                var island = overlay.FindControl<Border>("IslandBackground")!;
                var track = overlay.FindControl<Panel>("NotificationTrack")!;
                foreach (var (key, text) in new[] { ("preparing", "请做好上课准备"), ("break", "下课了") })
                {
                    bool Fits() => Math.Abs(island.Bounds.Width - Math.Ceiling(card.Bounds.Width + 2 * card.Margin.Left)) <= 1.5;
                    for (var i = 0; i < 30 && !Fits(); i++) await Task.Delay(40);
                    Check(Fits(), $"Normal mask fits {card.Bounds.Width:F1} plus margins {card.Margin.Left:F1} in {island.Bounds.Width:F1} at {dpi} dpi");
                    overlay.ShowNotificationMask(text, lifetime: TimeSpan.FromSeconds(15));
                    Check(track.IsVisible && track.Width > 0, "Reminder track starts with the mask: " + key);
                    overlay.ShowNotificationOverlay("下节课", "信息技术与综合实践", "", "08:00–08:45", "", text, true, TimeSpan.FromSeconds(15));
                    await Task.Delay(300);
                    var subject = overlay.FindControl<TextBlock>("NotificationOverlaySubject")!;
                    Check(subject.IsEffectivelyVisible && subject.Text == "信息技术与综合实践", $"Full subject appears for {key} at {dpi} dpi");
                    overlay.HideNotificationMask();
                    for (var i = 0; i < 45 && (track.IsVisible || card.Opacity < .99 || !Fits()); i++) await Task.Delay(40);
                    Check(!track.IsVisible && card.Opacity >= .99 && Fits(),
                        $"After {key}, mask {island.Bounds.Width:F1} fits widgets {card.Bounds.Width:F1} with inset {card.Margin.Left:F1} at {dpi} dpi (visible={card.Opacity:F1}, track={track.IsVisible})");
                }
            }
            async Task Shot(Window window, string name, string mode, int dpi)
            {
                await Task.Delay(220);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                var bounds = window.Bounds;
                var scale = window.RenderScaling;
                var size = PixelSize.FromSize(bounds.Size, scale);
                if (!window.IsVisible || size.Width < 24 || size.Height < 24) throw new InvalidOperationException($"No live content: {name}");
                using var bitmap = new RenderTargetBitmap(size, new Vector(dpi, dpi));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"{name}-{mode}-{dpi}.png"));
                var text = window.GetVisualDescendants().OfType<TextBlock>().Count(b => b.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(b.Text));
                if (text == 0) throw new InvalidOperationException($"No readable text: {name}");
                captures.Add(new Capture(name, mode, scale, size.Width, size.Height, text));
            }
            async Task CheckDesktopControls()
            {
                OpenSettings("appearance");
                await Task.Delay(220);
                var view = _settingsWindow!;
                var slider = view.FindControl<Slider>("OpacitySlider") ?? throw new InvalidOperationException("Opacity slider missing");
                var initial = slider.Value;
                var newValue = initial > 0.7 ? 0.4 : 0.8;
                slider.Value = newValue;
                await Task.Delay(250);
                Check(slider.Value != initial && Math.Abs(SettingsService.Load().BackgroundOpacity - newValue) < 0.01, "Appearance slider persists its new value");
                slider.Value = initial;
                view.NavigateTo("notification");
                await Task.Delay(250);
                var toggle = view.FindControl<Avalonia.Controls.ToggleSwitch>("PreparingEnabledCheck") ?? throw new InvalidOperationException("Preparing toggle missing");
                var old = toggle.IsChecked;
                toggle.IsChecked = !(old ?? false);
                await Task.Delay(250);
                Check(toggle.IsChecked != old && SettingsService.Load().Notification.IsClassOnPreparingNotificationEnabled == toggle.IsChecked,
                    "Notification toggle persisted to anonymous data");
                toggle.IsChecked = old;
                var scroll = view.FindControl<ScrollViewer>("PageScroll") ?? throw new InvalidOperationException("Scroll host missing");
                scroll.ScrollToEnd();
                await Task.Delay(120);
                Check(scroll.Offset.Y > 0, "Long page scrolls to additional settings");
                view.NavigateTo("home");
                await Task.Delay(220);
                var back = view.FindControl<Button>("BackButton") ?? throw new InvalidOperationException("Missing navigation back");
                back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(220);
                Check(view.CurrentPageKey == "notification", "Back returned to the previously visited category");
                Check(scroll.Offset.Y < 1, "Revisited settings open at the top");
                view.NavigateTo("plugins");
                await Task.Delay(200);
                var rows = view.FindControl<StackPanel>("PluginRows") ?? throw new InvalidOperationException("Plugin controls missing");
                var log = Path.Combine(SettingsService.DataDirectory, "PluginSettings", "plugins", "fixture", "lifecycle.txt");
                foreach (var enable in new[] { true, false })
                {
                    var switcher = rows.GetVisualDescendants().OfType<Avalonia.Controls.ToggleSwitch>().Single(s => s.IsEffectivelyVisible);
                    Check(switcher.IsEnabled && switcher.IsChecked != enable, "Plugin switch ready: " + enable);
                    switcher.IsChecked = enable;
                    var expectedStatus = enable ? "已加载" : "已禁用";
                    for (var i = 0; i < 100 && AppServices.Plugins!.LoadedPlugins.Single().Status != expectedStatus; i++) await Task.Delay(40);
                    await Task.Delay(120);
                    Check(AppServices.Plugins!.LoadedPlugins.Single().Status == expectedStatus, "Settings switch changed plugin lifecycle: " + expectedStatus);
                }
                Check(File.ReadAllLines(log).SequenceEqual(new[] { "init", "stop" }), "Plugin settings called real DLL once per transition");
                view.NavigateTo("appearance");
                slider.Value = .65;
                view.Close();
                Check(Math.Abs(SettingsService.Load().BackgroundOpacity - .65) < .001, "Closing settings immediately commits the last slider change");
                OpenSettings("home");
            }
        }
        catch (Exception ex) { checks.Add(ex.ToString()); exitCode = 1; }
        finally
        {
            using var process = Process.GetCurrentProcess();
            var report = new { Passed = exitCode == 0, Checks = checks, Captures = captures,
                ElapsedMs = ticket.ElapsedMilliseconds, WorkingSetBytes = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false) };
            await File.WriteAllTextAsync(Path.Combine(directory, "review.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (exitCode != 0 || Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_KEEP_OPEN") != "1")
                desktop.Shutdown(exitCode);
        }
    }

    private static void SetReviewDpi(Window window, int dpi)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Missing native handle");
        if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("Cannot measure native window");
        var ratio = dpi / (96 * window.RenderScaling);
        rect.Right = rect.Left + (int)Math.Round((rect.Right - rect.Left) * ratio);
        rect.Bottom = rect.Top + (int)Math.Round((rect.Bottom - rect.Top) * ratio);
        _ = SendMessageW(handle, 0x02E0, dpi | (dpi << 16), ref rect);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out RectNative rect);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint msg, nint dpi, ref RectNative rect);
}
#endif
