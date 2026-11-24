#if DEBUG
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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
        if (i < 0 || i + 1 >= args.Length || _mode == OobeMode.SettingsPreview) return;
        var mode = args[i + 1];
        var preview = _mode == OobeMode.StandalonePreview;
        var directory = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var marker = Path.Combine(directory, ".classnex-oobe-review");
        if (mode is not ("measure" or "reduced" or "finish" or "close" or "migration-keep" or "migration-recycle" or "preview-finish" or "preview-close" or "preview-import") ||
            mode.StartsWith("preview-", StringComparison.Ordinal) != preview || !File.Exists(marker) || File.ReadAllText(marker) != "anonymous-oobe-v1") return;
        var s = AppServices.Settings;
        if (s.IsOobeCompleted != preview || s.IsNexVoiceWakeEnabled || s.IsNexEdgeBarEnabled || s.IsNexIntelligenceEnabled || s.CentralManagement.IsEnabled ||
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
        var originalSettings = preview ? File.ReadAllBytes(SettingsService.SettingsPath) : null;
        var registeredAtStart = StartupRegistry.IsRunAtStartupEnabled();
        var pages = new Control[] { PageWelcome, PageData, PageImport, PagePreferences, PageAccount, PageMore, PageFinish };
        bool At(int p) => pages[p].IsEffectivelyVisible && pages.Count(x => x.IsEffectivelyVisible) == 1;
        static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Save()
        {
            File.WriteAllText(report + ".tmp", JsonSerializer.Serialize(new { mode, systemAnimations, error, timing, checks }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(report + ".tmp", report, true);
        }

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
            await Observe(() => At(target) && IsNavigationSettled, "step " + target);
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
            if (!baseline && mode == "migration-keep") checks["firstRunTrayStartsWithWizardCommands"] = App.ReviewFirstRunTray(this);
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
            checks["burstStopsOnData"] = At(1) && !NextButton.IsEffectivelyVisible;
            if (!baseline && mode == "measure")
            {
                Click(NewDeviceButton); Click(BackButton); Click(ImportDeviceButton);
                checks["differentButtonsRespondDuringPageMotion"] = At(2);
                await Step(1, BackButton);
            }
            if (preview)
            {
                if (mode == "preview-import")
                {
                    await Step(2, ImportDeviceButton);
                    var source = Path.Combine(directory, "source", "ClassIsland");
                    var sourceSettings = Path.Combine(source, "data", "Settings.json");
                    var before = File.ReadAllBytes(sourceSettings);
                    await InspectMigrationAsync(Path.Combine(source, "ClassIsland.exe"));
                    if (_importPlan is null) throw new InvalidOperationException("Preview preflight failed: " + _migrationError);
                    await RunImportAsync();
                    checks["previewImportWithDefaultCleanupLeavesBothInstallsUntouched"] = _migrationFinished && CleanupToggle.IsChecked == true &&
                        File.Exists(Path.Combine(source, "ClassIsland.exe")) && before.SequenceEqual(File.ReadAllBytes(sourceSettings)) &&
                        !Directory.EnumerateDirectories(SettingsService.DataDirectory, "ClassIsland-*").Any() && ReferenceEquals(s, AppServices.Settings);
                    await Step(3, NextButton);
                }
                else await Step(3, NewDeviceButton);
                ThemeCombo.SelectedIndex = 2;
                RunAtStartupCheck.IsChecked = true;
                await Step(4, NextButton);
                Click(OobeLoginButton); Click(OobeLogoutButton);
                checks["previewCannotAuthorizeOrSignOut"] = !OobeLoginButton.IsEnabled && !AccountService.FlowActive && AccountService.Current is null;
                if (mode == "preview-close") return;
                await Step(5, NextButton);
                VoiceWakeCheck.IsChecked = true;
                await Step(6, NextButton, burst: true);
                finish = true;
                return;
            }
            if (mode.StartsWith("migration-", StringComparison.Ordinal))
            {
                var sourceRoot = Path.Combine(directory, "source", "ClassIsland");
                var exe = Path.Combine(sourceRoot, "ClassIsland.exe");
                var folder = new DirectoryInfo(directory);
                var sourceSettings = Path.Combine(sourceRoot, "data", "Settings.json");
                if (!File.Exists(exe) || !File.Exists(sourceSettings) || folder.Parent?.Name != "build" || !folder.Name.StartsWith("oobe-review-", StringComparison.Ordinal) ||
                    !Guid.TryParseExact(folder.Name[12..], "N", out _) || new[] { directory, Path.Combine(directory, "source"), sourceRoot, Path.GetDirectoryName(sourceSettings)!, sourceSettings }
                        .Any(p => File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)))
                    throw new InvalidOperationException("Missing anonymous migration fixture.");
                void Shot(string name)
                {
                    UpdateLayout();
                    var pixels = Avalonia.PixelSize.FromSize(Bounds.Size, RenderScaling);
                    using var shot = new Avalonia.Media.Imaging.RenderTargetBitmap(pixels, new Avalonia.Vector(96 * RenderScaling, 96 * RenderScaling));
                    shot.Render(this);
                    shot.Save(Path.Combine(directory, name + ".png"));
                }
                Rect InWizard(Visual visual)
                {
                    var origin = visual.TranslatePoint(default, this) ?? throw new InvalidOperationException("Migration feedback is not attached to the wizard.");
                    return new Rect(origin, visual.Bounds.Size);
                }
                bool BusyLayoutIsClear()
                {
                    UpdateLayout();
                    var title = InWizard(ImportStatus);
                    var row = InWizard(ImportProgressRow);
                    var rail = InWizard(ImportProgress);
                    return ImportStatus.IsEffectivelyVisible && ImportProgress.IsEffectivelyVisible && !ImportDetails.IsEffectivelyVisible &&
                        !ImportNotes.IsEffectivelyVisible && !CleanupSummary.IsEffectivelyVisible && ImportProgress.IsArrangeValid &&
                        title.Height > 0 && rail.Height > 0 && rail.Width > 0 && row.Top >= title.Bottom + 4 &&
                        row.Top <= rail.Top && rail.Bottom <= row.Bottom && PageImport.GetVisualDescendants().OfType<TextBlock>()
                            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) && text.Bounds.Width > 0 && text.Bounds.Height > 0)
                            .All(text => !InWizard(text).Intersects(rail));
                }
                Shot("data-and-migration");
                await Step(2, ImportDeviceButton);
                checks["sameWindowOptionalStep"] = TryGetPlatformHandle()?.Handle == hwnd && !NextButton.IsEffectivelyEnabled && !ImportAdvanced.IsExpanded && CleanupToggle.IsChecked == true;
                await InspectMigrationAsync(Path.Combine(directory, "not-classisland.exe"));
                checks["badSourceStaysOnImportAndOffersRetry"] = At(2) && _migrationError is not null && BrowseImportButton.IsEffectivelyEnabled && !NextButton.IsEffectivelyEnabled && Directory.Exists(sourceRoot);
                var inspection = InspectMigrationAsync(exe);
                CancelMigration();
                await inspection;
                checks["cancelledInspectionDoesNotTouchSource"] = _importPlan is null && File.Exists(exe) && _migrationOperation is null;
                await InspectMigrationAsync(exe);
                checks["preflightReadsOnlyAndReportsSkippedWidget"] = _importPlan is { WidgetCount: 3, SkippedWidgetCount: 1 } && NextButton.IsEffectivelyEnabled && File.Exists(exe) && !AppServices.Settings.IsOobeCompleted;
                if (_importPlan is null) throw new InvalidOperationException("Preflight failed: " + _migrationError);
                checks["recyclePathVisibleBeforeCommit"] = CleanupSummary.Text?.Contains(sourceRoot, StringComparison.OrdinalIgnoreCase) == true;
                ImportAdvanced.IsExpanded = true;
                CleanupToggle.IsChecked = mode == "migration-recycle";
                await Task.Delay(400);
                Shot("migration-ready");
                checks["readyShowsCountsWithoutAProgressRail"] = ImportDetails.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(ImportDetails.Text) && !ImportProgress.IsEffectivelyVisible;
                var savedBefore = await File.ReadAllBytesAsync(SettingsService.SettingsPath);
                await File.AppendAllTextAsync(sourceSettings, "\n");
                await RunImportAsync();
                var savedAfter = await File.ReadAllBytesAsync(SettingsService.SettingsPath);
                checks["changedSourceKeepsBothSidesAndRequiresNewInspection"] = At(2) && !_migrationFinished && _migrationError is not null &&
                    _importPlan is null && File.Exists(exe) && BrowseImportButton.IsEffectivelyEnabled && !NextButton.IsEffectivelyEnabled && savedBefore.SequenceEqual(savedAfter);
                await InspectMigrationAsync(exe);
                if (_importPlan is null) throw new InvalidOperationException("Retry preflight failed: " + _migrationError);
                var cancelRun = RunImportAsync();
                var couldCancel = BackButton.IsEffectivelyEnabled && Equals(BackButton.Content, "取消");
                Click(BackButton);
                await cancelRun;
                var savedAfterCancel = await File.ReadAllBytesAsync(SettingsService.SettingsPath);
                checks["cancelBeforeCommitPreservesSourceAndSettingsAndAllowsRetry"] = couldCancel && At(2) && !_migrationFinished &&
                    _importPlan is not null && _migrationOperation is null && File.Exists(exe) && NextButton.IsEffectivelyEnabled && savedBefore.SequenceEqual(savedAfterCancel);
                var run = RunImportAsync();
                var firstOperation = _migrationOperation;
                var duplicate = RunImportAsync();
                checks["doubleStartDoesNotCreateASecondImport"] = duplicate.IsCompletedSuccessfully && firstOperation is not null && ReferenceEquals(firstOperation, _migrationOperation);
                checks["progressVisibleWhileImporterRuns"] = ImportProgress.IsEffectivelyVisible && !NextButton.IsEffectivelyEnabled;
                checks["busyProgressHasItsOwnRowClearOfAllVisibleText"] = BusyLayoutIsClear();
                Shot("migration-working");
                var fullTitleHeight = ImportStatus.Bounds.Height;
                var feedbackWidth = ImportFeedback.MaxWidth;
                try
                {
                    ImportFeedback.MaxWidth = 100;
                    checks["wrappedBusyTitleStillClearsTheRail"] = BusyLayoutIsClear() && ImportStatus.Bounds.Height > fullTitleHeight;
                }
                finally { ImportFeedback.MaxWidth = feedbackWidth; UpdateLayout(); }
                await run;
                checks["verifiedImportAppliesThemeAndSelectedCleanup"] = _migrationFinished && ThemeCombo.SelectedIndex == 2 && AppServices.Settings.ThemeMode == "dark" && Directory.Exists(sourceRoot) == (mode == "migration-keep");
                if (!_migrationFinished) throw new InvalidOperationException("Commit failed: " + _migrationError);
                await Task.Delay(300);
                var expected = Color.Parse("#FFB6377E");
                var liveBrush = NextButton.Background as ISolidColorBrush;
                var hasAccent = this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var accent);
                checks["importedAccentReachesLiveWizardButton"] = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark &&
                    hasAccent && accent is Color liveAccent && liveAccent == expected &&
                    this.TryFindResource("AccentFillColorDefaultBrush", ActualThemeVariant, out var fill) && fill is ISolidColorBrush resolved &&
                    liveBrush?.Color == resolved.Color && resolved.Color.R > resolved.Color.G && resolved.Color.B > resolved.Color.G;
                var sample = NextButton.TranslatePoint(new Point(12, NextButton.Bounds.Height / 2), this) ?? throw new InvalidOperationException("The receipt button is not in the wizard.");
                File.WriteAllText(Path.Combine(directory, "migration-accent-sample.json"), JsonSerializer.Serialize(new { x = (int)Math.Round(sample.X * RenderScaling), y = (int)Math.Round(sample.Y * RenderScaling), accent = accent?.ToString(), button = liveBrush?.Color.ToString() }));
                Shot("migration-result");
                checks["receiptShowsCountsWithoutAProgressRail"] = ImportDetails.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(ImportDetails.Text) && !ImportProgress.IsEffectivelyVisible;
                await Step(3, NextButton);
                await Step(2, BackButton);
                checks["backShowsReceiptOnly"] = !BrowseImportButton.IsEnabled && !BackButton.IsEffectivelyVisible && _migrationFinished;
                await Step(3, NextButton);
                await Step(4, NextButton);
                await Step(5, NextButton);
                await Step(6, NextButton);
                checks["finishReflectsImportedClassroomVisibility"] = AppServices.Settings.IsMainWindowVisible == (mode == "migration-recycle") &&
                    FinishBody.Text?.Contains(mode == "migration-recycle" ? "显示课表浮窗" : "隐藏状态", StringComparison.Ordinal) == true;
                finish = true;
                return;
            }
            await Step(3, NewDeviceButton);
            if (mode == "finish") SemesterPicker.SelectedDate = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
            if (mode == "reduced")
            {
                ThemeCombo.SelectedIndex = 2;
                RunAtStartupCheck.IsChecked = true;
                checks["draftPreferencesNotCommitted"] = s.ThemeMode == "light" && s.Language == "zh-CN" && !s.RunAtStartup &&
                    ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark && Equals(NextButton.Content, "Continue");
            }
            await Step(1, BackButton);
            await Step(0, BackButton);
            await Step(1, NextButton);
            await Step(3, NewDeviceButton);
            await Step(4, NextButton);
            checks["visitingAccountDoesNotAuthorize"] = !AccountService.FlowActive && AccountService.Current is null;
            await Step(5, NextButton);
            checks["voiceInitiallyOff"] = VoiceWakeCheck.IsChecked == false;
            await Step(4, BackButton);
            await Step(5, NextButton);
            await Step(6, NextButton, burst: !baseline);
            checks["finishVisibleAndDesktopNotInitialized"] = At(6) && !s.IsOobeCompleted && (baseline ||
                (AppServices.MainWindow is null && AppServices.Plugins is null && AppServices.NotificationService is null && AppServices.Management is null));
            if (!baseline) checks["rapidLastNextDidNotFinish"] = IsVisible && !AppServices.Settings.IsOobeCompleted;
            checks["noMicrophoneCoordinatorBeforeFinish"] = AppServices.NexVoice is null;
            finish = mode == "finish";
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            try
            {
                if (finish && error is null)
                {
                    Click(NextButton); Click(NextButton);
                    var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    var classroom = AppServices.MainWindow;
                    var visible = mode != "migration-keep";
                    checks["finishReplacesWizardWithClassroom"] = _closed && !IsVisible && classroom != null &&
                        ReferenceEquals(lifetime?.MainWindow, classroom) && lifetime!.ShutdownMode == ShutdownMode.OnExplicitShutdown && classroom.IsVisible == visible;
                    checks["classroomServicesConnected"] = AppServices.NotificationService != null && AppServices.Plugins != null && AppServices.Management != null;
                    if (!visible && classroom is not null)
                    {
                        var recovered = App.ReviewHiddenClassroomTray(classroom);
                        checks["hiddenClassroomCanBeRestoredAndHiddenFromTray"] = recovered;
                    }
                    if (mode.StartsWith("migration-", StringComparison.Ordinal))
                        checks["liveClassroomHasImportedProfileAndBreak"] = string.Equals(AppServices.Schedule.FilePath, AppServices.Settings.TimetablePath, StringComparison.OrdinalIgnoreCase) &&
                            AppServices.Schedule.Profile.Schedules.SelectMany(day => day.Classes).Any(course => course.Subject == "地理") &&
                            AppServices.TimeLayout.Layout.Times.Any(time => time.Name == "晨间休息");
                }
                else checks["closingDoesNotStartTheClassroom"] = AppServices.MainWindow is null && AppServices.Management is null;
                if (originalSettings is not null)
                    checks["readOnlyPreviewKeepsSettingsAndRegistration"] = originalSettings.SequenceEqual(File.ReadAllBytes(SettingsService.SettingsPath)) &&
                        !s.IsNexVoiceWakeEnabled && !s.RunAtStartup && s.ThemeMode == "light" && registeredAtStart == StartupRegistry.IsRunAtStartupEnabled();
            }
            catch (Exception ex) { error ??= ex.ToString(); }
            timing["probeFinishedMs"] = elapsed.ElapsedMilliseconds;
            Save();
            if (!_closed && (!finish || error != null)) Close();
            if (finish && _closed && error is null && !baseline)
            {
                var timer = Stopwatch.StartNew();
                var receipt = Path.Combine(directory, ".desktop-observed");
                while (!File.Exists(receipt) && timer.Elapsed < TimeSpan.FromSeconds(75)) await Task.Delay(80);
                App.ExitApp();
            }
        }
    }
}
#endif
