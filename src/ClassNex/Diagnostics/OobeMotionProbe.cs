#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class OobeWindow
{
    partial void SuppressMotionForProbe(ref bool suppressed);

    private async void RunMotionProbe()
    {
        const string option = "--review-oobe-motion";
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, option);
        if (i < 0 || i + 1 >= args.Length || _preview) return;
        var mode = args[i + 1];
        var directory = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var marker = Path.Combine(directory, ".classnex-oobe-review");
        if (mode is not ("measure" or "reduced" or "finish" or "close") || !File.Exists(marker) || File.ReadAllText(marker) != "anonymous-oobe-v1") return;
        var s = AppServices.Settings;
        if (s.IsOobeCompleted || s.IsNexVoiceWakeEnabled || s.IsNexEdgeBarEnabled || s.IsNexIntelligenceEnabled || s.CentralManagement.IsEnabled ||
            s.RunAtStartup || s.RegisterUrlProtocol || !string.IsNullOrEmpty(s.UserName) || !string.IsNullOrEmpty(s.Email) || AccountService.Current is not null)
            throw new InvalidOperationException("Onboarding motion review requires a new anonymous, offline fixture.");

        var elapsed = Stopwatch.StartNew();
        var timing = new Dictionary<string, long>();
        var checks = new Dictionary<string, bool>();
        var systemAnimations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var baseline = Array.IndexOf(args, "--baseline") >= 0;
        if (mode == "reduced")
        {
            var suppressed = false;
            SuppressMotionForProbe(ref suppressed);
            checks["localReducedMotionPolicyActive"] = suppressed;
        }
        string? error = null;
        bool finish = false;
        var report = Path.Combine(directory, "oobe-motion.json");
        var pages = new Control[] { PageWelcome, PagePreferences, PageAccount, PageMore, PageFinish };
        bool At(int p) => pages[p].IsEffectivelyVisible && pages.Count(x => x.IsEffectivelyVisible) == 1;
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Save() => File.WriteAllText(report, JsonSerializer.Serialize(new { mode, systemAnimations, error, timing, checks }, new JsonSerializerOptions { WriteIndented = true }));

        async Task Observe(Func<bool> predicate, string label)
        {
            var timeout = Stopwatch.StartNew();
            while (!predicate())
            {
                if (!IsVisible || timeout.ElapsedMilliseconds > 6500) throw new InvalidOperationException("Timed out: " + label);
                await Task.Delay(12);
            }
        }
        async Task Step(int target, Button button, bool burst = false)
        {
            var start = elapsed.ElapsedMilliseconds;
            Click(button);
            if (burst) { Click(button); Click(button); }
            await Observe(() => At(target) && !_pageSwitching, "step " + target);
            timing[$"step{target}ReadyAt{start}"] = elapsed.ElapsedMilliseconds - start;
        }
        try
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Missing wizard window handle.");
            File.WriteAllText(Path.Combine(directory, "wizard.json"), JsonSerializer.Serialize(new { pid = Environment.ProcessId, hwnd = hwnd.ToInt64() }));
            var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RequestAnimationFrame(_ => first.TrySetResult());
            await first.Task.WaitAsync(TimeSpan.FromSeconds(6));
            timing["openedToFirstFrameCallbackMs"] = elapsed.ElapsedMilliseconds;
            checks["chineseFirstGreeting"] = GreetingText.Text == "你好";
            checks["textAndFooterReadableOnFirstFrame"] = WelcomeTextPanel.Opacity >= .75 && FooterActions.Opacity >= .75 && NextButton.IsEffectivelyEnabled;
            if (!baseline)
                checks["firstRunDefersClassroomNotificationPluginsAndCentral"] = AppServices.MainWindow is null && AppServices.NotificationService is null && AppServices.Plugins is null && AppServices.Management is null;
            await Observe(() => WelcomeTextPanel.Opacity >= .75, "readable welcome text");
            timing["openedToReadableWelcomeMs"] = elapsed.ElapsedMilliseconds;
            await Observe(() => FooterActions.Opacity >= .75 && NextButton.IsEffectivelyEnabled, "readable footer");
            timing["openedToReadableFooterMs"] = elapsed.ElapsedMilliseconds;
            if (mode == "close") return;
            await Task.Delay(1600);
            await Observe(() => WelcomeImage.Background is ImageBrush { Source: not null }, "classroom artwork");
            checks["welcomeArtworkReady"] = true;
            if (mode == "reduced")
            {
                checks["reducedMotionKeepsGreetingStill"] = GreetingText.Text == "你好";
                LanguageCombo.SelectedIndex = 1;
            }
            await Step(1, NextButton, burst: true);
            checks["burstStopsOnPreferences"] = At(1);
            if (mode == "finish") SemesterPicker.SelectedDate = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
            if (mode == "reduced")
            {
                ThemeCombo.SelectedIndex = 2;
                RunAtStartupCheck.IsChecked = true;
                checks["draftPreferencesNotCommitted"] = s.ThemeMode == "light" && s.Language == "zh-CN" && !s.RunAtStartup &&
                    ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark && Equals(NextButton.Content, "Continue");
            }
            await Step(0, BackButton);
            await Step(1, NextButton);
            await Step(2, NextButton);
            checks["visitingAccountDoesNotAuthorize"] = !AccountService.FlowActive && AccountService.Current is null;
            await Step(3, NextButton);
            checks["voiceInitiallyOff"] = VoiceWakeCheck.IsChecked == false;
            await Step(2, BackButton);
            await Step(3, NextButton);
            await Step(4, NextButton, burst: !baseline);
            checks["finishVisibleAndDesktopNotInitialized"] = At(4) && !s.IsOobeCompleted && (baseline ||
                (AppServices.MainWindow is null && AppServices.Plugins is null && AppServices.NotificationService is null && AppServices.Management is null));
            if (!baseline) checks["rapidLastNextDidNotFinish"] = IsVisible && !AppServices.Settings.IsOobeCompleted;
            checks["noMicrophoneCoordinatorBeforeFinish"] = AppServices.NexVoice is null;
            finish = mode == "finish";
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            timing["probeFinishedMs"] = elapsed.ElapsedMilliseconds;
            Save();
            if (finish && error is null) { Click(NextButton); Click(NextButton); }
            else Close();
        }
    }
}
#endif
