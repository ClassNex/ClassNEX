#if DEBUG
using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    internal static bool ReviewFirstRunTray(OobeWindow guide)
    {
        if (Current is not App app || !ReferenceEquals(guide, app._oobeWindow) || app._trayIcon is null) return false;
        app.EnsureTrayMenu();
        return app._trayMenu!.GetVisualDescendants().OfType<Button>().Select(AutomationProperties.GetName).SequenceEqual(["继续首次设置", "退出"]);
    }

    internal static bool ReviewHiddenClassroomTray(MainWindow classroom)
    {
        if (Current is not App app || app._oobeWindow is not null || !ReferenceEquals(app._mainWindow, classroom) ||
            classroom.IsVisible || app._trayIcon is not { } icon) return false;
        app.ShowTrayMenu(icon.IconBounds(), false);
        var toggle = app._trayMenu!.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => AutomationProperties.GetName(b) == "显示 / 隐藏主界面");
        if (toggle is null) return false;
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var shown = classroom.IsVisible && AppServices.Settings.IsMainWindowVisible;
        app.ShowTrayMenu(icon.IconBounds(), false);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return shown && !classroom.IsVisible && !AppServices.Settings.IsMainWindowVisible;
    }

    private async Task ReviewOobePreviewAsync(IClassicDesktopStyleApplicationLifetime desktop, MainWindow classroom, ClassNex.WinUI.NativeSettingsWindow settings,
        Func<Window, string, string, int, Task> shot, Action<bool, string> check)
    {
        if (AccountService.Current != null || AccountService.FlowActive || AppServices.NexVoice != null || !AppServices.Settings.IsOobeCompleted ||
            !File.Exists(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review")))
            throw new InvalidOperationException("Onboarding preview review requires an isolated, anonymous fixture without a microphone coordinator");
        var original = AppServices.Settings.IsNexVoiceWakeEnabled;
        var initialPreferences = (AppServices.Settings.Language, AppServices.Settings.ThemeMode, AppServices.Settings.RunAtStartup);
        var registered = StartupRegistry.IsRunAtStartupEnabled();
        var runtime = _runtime;
        var notifications = AppServices.NotificationService;
        var category = settings.CurrentPageKey;
        try
        {
            foreach (var saved in new[] { original, !original })
            {
                AppServices.Settings.IsNexVoiceWakeEnabled = saved;
                SettingsService.Save(AppServices.Settings);
                ShowOobePreview();
                var guide = _oobePreview ?? throw new InvalidOperationException("Onboarding preview did not open");
                try
                {
                    T Element<T>(string name) where T : Control => guide.FindControl<T>(name) ?? throw new InvalidOperationException(name);
                    var panels = new[] { "PageWelcome", "PageData", "PageImport", "PagePreferences", "PageAccount", "PageMore", "PageFinish" }.Select(Element<Control>).ToArray();
                    var next = Element<Button>("NextButton");
                    var back = Element<Button>("BackButton");
                    var toggle = Element<ToggleSwitch>("VoiceWakeCheck");
                    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    async Task Step(int target, Button? button = null)
                    {
                        if (button != null) Click(button);
                        var start = Stopwatch.GetTimestamp();
                        while (!guide.IsNavigationSettled || !panels[target].IsEffectivelyVisible || panels.Count(p => p.IsEffectivelyVisible) != 1)
                        {
                            if (Stopwatch.GetElapsedTime(start).TotalSeconds >= 5) throw new InvalidOperationException("Onboarding navigation stalled at " + panels[target].Name);
                            await Task.Delay(30);
                        }
                    }
                    Task Capture(string name) => shot(guide, name, guide.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light", (int)Math.Round(guide.RenderScaling * 96));
                    await Step(0);
                    await Step(1, next);
                    var first = Element<Button>("NewDeviceButton");
                    var migrate = Element<Button>("ImportDeviceButton");
                    var hwnd = guide.TryGetPlatformHandle()?.Handle;
                    check(first.IsEffectivelyVisible && migrate.IsEffectivelyVisible && !next.IsEffectivelyVisible, "The data step offers two direct choices, not a third continue button");
                    if (saved == original) await Capture("oobe-data");
                    await Step(2, migrate);
                    var advanced = Element<Expander>("ImportAdvanced");
                    var cleanup = Element<ToggleSwitch>("CleanupToggle");
                    check(guide.TryGetPlatformHandle()?.Handle == hwnd && !advanced.IsExpanded && cleanup.IsChecked == true && !next.IsEffectivelyEnabled,
                        "Import is a subpage in the same window; no file is preselected and cleanup defaults on inside the collapsed advanced section");
                    advanced.IsExpanded = true;
                    cleanup.IsChecked = false;
                    if (saved == original) await Capture("oobe-migration");
                    await Step(1, back);
                    await Step(3, first);
                    check(!panels[2].IsEffectivelyVisible, "New device goes straight to preferences without entering the import subpage");
                    Element<ToggleSwitch>("RunAtStartupCheck").IsChecked = !initialPreferences.RunAtStartup;
                    if (saved != original)
                    {
                        await Step(1, back);
                        await Step(0, back);
                        Element<ComboBox>("LanguageCombo").SelectedIndex = initialPreferences.Language == "en-US" ? 0 : 1;
                        await Step(1, next);
                        await Step(3, first);
                        Element<ComboBox>("ThemeCombo").SelectedIndex = initialPreferences.ThemeMode == "dark" ? 1 : 2;
                    }
                    await Step(4, next);
                    var signIn = Element<Button>("OobeLoginButton");
                    var signOut = Element<Button>("OobeLogoutButton");
                    Click(signIn); Click(signOut);
                    check(!signIn.IsEnabled && !signOut.IsVisible, "Read-only setup preview does not offer account changes");
                    check(!AccountService.FlowActive && AccountService.Current is null, "Visiting onboarding account step does not authorize or open a browser");
                    await Step(5, next);
                    check(toggle.IsEffectivelyVisible && toggle.IsEffectivelyEnabled && toggle.IsChecked == saved,
                        $"Voice setup loads the saved wake preference: {saved}");
                    toggle.IsChecked = !saved;
                    if (saved == original) await Capture("oobe-voice-preview");
                    await Step(6, next);
                    if (saved == original)
                    {
                        await Step(5, back);
                        check(toggle.IsChecked == !saved, "Preview back-navigation keeps the unsaved voice switch position");
                        guide.Close();
                    }
                    else Click(next);
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    check(_oobePreview is null && !guide.IsVisible && AppServices.Settings.IsNexVoiceWakeEnabled == saved &&
                        SettingsService.Load().IsNexVoiceWakeEnabled == saved, $"Closing or finishing the preview does not commit the edited preference: {saved}");
                    var current = AppServices.Settings;
                    var disk = SettingsService.Load();
                    check((current.Language, current.ThemeMode, current.RunAtStartup) == initialPreferences &&
                        (disk.Language, disk.ThemeMode, disk.RunAtStartup) == initialPreferences && StartupRegistry.IsRunAtStartupEnabled() == registered,
                        $"Preview does not persist language, appearance or Windows startup registration: {saved}");
                    check(ReferenceEquals(desktop.MainWindow, classroom) && ReferenceEquals(_desktop?.Settings, settings) &&
                        ReferenceEquals(_runtime, runtime) && ReferenceEquals(AppServices.NotificationService, notifications) &&
                        classroom.IsVisible && settings.AppWindow.IsVisible, "Closing a preview preserves the active classroom and settings windows");
                }
                finally { if (guide.IsVisible) guide.Close(); }
            }
            settings.NavigateTo("account");
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            check(settings.CurrentPageKey == "account", "Settings navigation still works after both onboarding previews close");
        }
        finally
        {
            AppServices.Settings.IsNexVoiceWakeEnabled = original;
            SettingsService.Save(AppServices.Settings);
            settings.NavigateTo(category);
        }
    }
}
#endif
