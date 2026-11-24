using System.Diagnostics;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaFluentUI.Windowing;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

internal enum OobeMode { FirstRun, SettingsPreview, StandalonePreview }

public partial class OobeWindow : AppWindow
{
    private enum Page { Welcome, Data, Import, Preferences, Account, More, Finish }
    private const int StepCount = 6;
    private static readonly TimeSpan RepeatTapGuard = TimeSpan.FromMilliseconds(110);
    private static readonly string[] Greetings =
    [
        "你好", "Hello", "こんにちは", "안녕하세요", "Bonjour", "Hola", "Hallo", "Привет",
        "Ciao", "Olá", "Xin chào", "สวัสดี", "مرحبا", "नमस्ते", "Merhaba", "Hej",
    ];

    private readonly OobeMode _mode;
    private bool IsPreview => _mode != OobeMode.FirstRun;
    internal event Action? SetupCompleted;
    private readonly bool _initialRunAtStartup;
    private readonly DispatcherTimer _greetingTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private CancellationTokenSource? _artworkLoad;
    private Bitmap? _artwork;
    private Page _page;
    private int _greetingIndex, _greetingEpoch;
    private long _lastStep;
    private Button? _lastStepButton;
    private bool _isEnglish, _finishing, _closed;
    internal bool IsNavigationSettled => _lastStep == 0 || Stopwatch.GetElapsedTime(_lastStep) >= RepeatTapGuard;
    private bool IsRepeatedTap(Button button) => ReferenceEquals(button, _lastStepButton) && !IsNavigationSettled;
#if DEBUG
    private bool _suppressProbeMotion;
    partial void SuppressMotionForProbe(ref bool suppressed)
    {
        _suppressProbeMotion = true;
        suppressed = !MotionEnabled;
    }
#endif

    private bool MotionEnabled => DesktopMotion.IsEnabled
#if DEBUG
        && !_suppressProbeMotion
#endif
        ;

    public OobeWindow() : this(OobeMode.FirstRun) { }

    internal OobeWindow(OobeMode mode)
    {
        _mode = mode;
        _initialRunAtStartup = AppServices.Settings.RunAtStartup;
        InitializeComponent();
        if (IsPreview) Topmost = true;
        LoadCurrentSettings();
        WireEvents();
        _greetingTimer.Tick += (_, _) => RotateGreeting();
#if DEBUG
        if (Enum.TryParse<Page>(Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_OOBE"), true, out var target) && Enum.IsDefined(target))
            Dispatcher.UIThread.Post(() => SwitchPage(target), DispatcherPriority.Background);
#endif
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
#if DEBUG
        RunMotionProbe();
#endif
        ArmGreeting();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_migrationOperation is null) return;
        e.Cancel = true;
        _closeAfterMigration = true;
        CancelMigration();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        ++_greetingEpoch;
        _greetingTimer.Stop();
        _artworkLoad?.Cancel();
        WelcomeImage.Background = null;
        _artwork?.Dispose();
        AccountService.Changed -= RefreshOobeAccount;
        base.OnClosed(e);
    }

    private void ArmGreeting()
    {
        var epoch = ++_greetingEpoch;
        _greetingTimer.Stop();
        _artworkLoad?.Cancel();
        if (_closed || _page != Page.Welcome) return;
        _greetingIndex = 0;
        GreetingText.Text = Greetings[0];
        RequestAnimationFrame(frameTime =>
        {
            if (_closed || !IsVisible || _page != Page.Welcome || epoch != _greetingEpoch) return;
            if (_artwork is null) _ = LoadArtworkAsync(epoch);
            if (MotionEnabled) _greetingTimer.Start();
        });
    }

    private async Task LoadArtworkAsync(int epoch)
    {
        using var load = new CancellationTokenSource();
        _artworkLoad = load;
        var width = Math.Clamp((int)Math.Ceiling(WelcomeImage.Bounds.Width * RenderScaling), 600, 1774);
        Bitmap? decoded = null;
        try
        {
            decoded = await Task.Run(() =>
            {
                using var stream = AssetLoader.Open(new Uri("avares://ClassNex/Assets/Oobe/Classroom.png"));
                return Bitmap.DecodeToWidth(stream, width);
            }, load.Token);
            if (load.IsCancellationRequested || _closed || epoch != _greetingEpoch) return;
            _artwork = decoded;
            decoded = null;
            WelcomeImage.Background = new ImageBrush(_artwork) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Left };
            if (MotionEnabled && ElementComposition.GetElementVisual(WelcomeImage) is { } visual) FadeIn(visual, .85f);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException) { Trace.WriteLine(e); }
        finally
        {
            decoded?.Dispose();
            if (ReferenceEquals(_artworkLoad, load)) _artworkLoad = null;
        }
    }

    private void RotateGreeting()
    {
        if (_closed || _page != Page.Welcome || !MotionEnabled)
        {
            _greetingTimer.Stop();
            return;
        }
        _greetingIndex = (_greetingIndex + 1) % Greetings.Length;
        GreetingText.Text = Greetings[_greetingIndex];
        if (ElementComposition.GetElementVisual(GreetingText) is { } visual) FadeIn(visual, .7f);
    }

    private void WireEvents()
    {
        NextButton.Click += async (_, _) =>
        {
            switch (_page)
            {
                case Page.Welcome: SwitchPage(Page.Data, NextButton); break;
                case Page.Import when _migrationFinished: SwitchPage(Page.Preferences, NextButton); break;
                case Page.Import when !IsRepeatedTap(NextButton): await RunImportAsync(); break;
                case Page.Preferences: SwitchPage(Page.Account, NextButton); break;
                case Page.Account: SwitchPage(Page.More, NextButton); break;
                case Page.More: SwitchPage(Page.Finish, NextButton); break;
                case Page.Finish: Finish(); break;
            }
        };

        BackButton.Click += (_, _) =>
        {
            if (_migrationOperation is not null) { CancelMigration(); return; }
            var previous = _page switch
            {
                Page.Data => Page.Welcome,
                Page.Import when !_migrationFinished => Page.Data,
                Page.Preferences => _migrationFinished ? Page.Import : Page.Data,
                Page.Account => Page.Preferences,
                Page.More => Page.Account,
                Page.Finish => Page.More,
                _ => _page,
            };
            SwitchPage(previous, BackButton);
        };
        NewDeviceButton.Click += (_, _) => { if (_page == Page.Data) SwitchPage(Page.Preferences, NewDeviceButton); };
        ImportDeviceButton.Click += (_, _) => { if (_page == Page.Data) SwitchPage(Page.Import, ImportDeviceButton); };
        BrowseImportButton.Click += async (_, _) => await BrowseImportAsync();
        CleanupToggle.IsCheckedChanged += (_, _) => RefreshMigrationPage();

        LanguageCombo.SelectionChanged += (_, _) =>
        {
            _isEnglish = LanguageCombo.SelectedIndex == 1;
            ApplyLanguage();
            RefreshOobeAccount();
        };

        PrivacyLink.Click += async (_, _) => await ShowPrivacyAsync();
        LicenseLink.Click += (_, _) => OpenUrl("https://github.com/ClassNex/ClassNEX/blob/main/LICENSE");

        OobeLoginButton.Click += (_, _) => StartOobeLogin();
        OobeLogoutButton.Click += (_, _) => { if (!IsPreview) AccountService.Logout(); };

        AccountService.Changed += RefreshOobeAccount;

        ThemeCombo.SelectionChanged += (_, _) =>
        {
            RequestedThemeVariant = ThemeCombo.SelectedIndex switch
            {
                1 => ThemeVariant.Light,
                2 => ThemeVariant.Dark,
                _ => PlatformSettings?.GetColorValues().ThemeVariant switch
                {
                    PlatformThemeVariant.Dark => ThemeVariant.Dark,
                    PlatformThemeVariant.Light => ThemeVariant.Light,
                    _ => ThemeVariant.Default,
                },
            };
        };
    }

    private void LoadCurrentSettings()
    {
        var s = AppServices.Settings;

        _isEnglish = string.Equals(s.Language, "en-US", StringComparison.OrdinalIgnoreCase);
        LanguageCombo.SelectedIndex = _isEnglish ? 1 : 0;

        ThemeCombo.SelectedIndex = s.ThemeMode switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };

        SemesterPicker.SelectedDate = s.SemesterStartDate ?? DefaultSemesterStart();
        RunAtStartupCheck.IsChecked = s.RunAtStartup;
        VoiceWakeCheck.IsChecked = s.IsNexVoiceWakeEnabled;

        ApplyLanguage();
        RefreshOobeAccount();
    }

    
    private static DateTime DefaultSemesterStart()
    {
        var today = DateTime.Today;
        var candidate = new DateTime(today.Year, 9, 1);
        return candidate < today ? candidate.AddYears(1) : candidate;
    }

    
    private void ApplyLanguage()
    {
        if (_isEnglish)
        {
            WelcomeBody.Text = "Welcome to ClassNEX — let's get started";
            Bullet1.Text = "• Selecting \"Agree and continue\" means you accept the privacy policy.";
            PrivacyLink.Content = "Privacy policy";
            LicenseLink.Content = "License (GPL-3.0)";
            LanguageLabel.Text = "Language";
            PreferencesTitle.Text = "Personalize";
            PreferencesBody.Text = "You can change all of these later in app settings.";
            ThemeLabel.Text = "Appearance";
            SemesterLabel.Text = "Term start date";
            RunAtStartupText.Text = "Start ClassNEX when Windows starts";
            AccountTitle.Text = "Account";
            AccountBody.Text = "Sign in or create an account securely in your browser. Your avatar, name and email are synced automatically.";
            OobeLoginButton.Content = "Sign in / Sign up in browser";
            OobeLogoutButton.Content = "Sign out";
            MoreTitle.Text = "Voice wake-up";
            MoreBody.Text = "Let NEX listen for its wake word after setup. You can also open NEX without a microphone.";
            VoiceWakeLabel.Text = "Listen for the wake word";
            FinishTitle.Text = "You're all set";
            BackButton.Content = "Back";
        }
        else
        {
            WelcomeBody.Text = "欢迎使用 ClassNEX，让我们开始吧";
            Bullet1.Text = "• 选择「同意并继续」即表示您同意隐私政策。";
            PrivacyLink.Content = "隐私政策";
            LicenseLink.Content = "开源许可（GPL-3.0）";
            LanguageLabel.Text = "选择语言";
            PreferencesTitle.Text = "个性化设置";
            PreferencesBody.Text = "这些设置稍后都能在「应用设置」里修改。";
            ThemeLabel.Text = "外观";
            SemesterLabel.Text = "开学日期";
            RunAtStartupText.Text = "开机时自动启动 ClassNEX";
            AccountTitle.Text = "账户";
            AccountBody.Text = "在浏览器中安全登录或注册，头像、用户名与邮箱会自动同步到本机。";
            OobeLoginButton.Content = "使用浏览器登录 / 注册";
            OobeLogoutButton.Content = "退出登录";
            MoreTitle.Text = "语音唤醒";
            MoreBody.Text = "开启后，NEX 会在向导完成后使用麦克风监听唤醒词。关闭也可以手动打开 NEX。";
            VoiceWakeLabel.Text = "监听唤醒词";
            FinishTitle.Text = "一切就绪";
            BackButton.Content = "上一步";
        }
        UpdateFinishMessage();
        SetMigrationLanguage();
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        var importing = _page == Page.Import;
        var canKeepSource = _cleanupStage is ClassIslandCleanupStage.CheckingPackage or ClassIslandCleanupStage.VerifyingSource;
        BackButton.IsVisible = _page != Page.Welcome && (!importing || !_migrationFinished || canKeepSource);
        BackButton.IsEnabled = !importing || !_pickerOpen && _cleanupStage != ClassIslandCleanupStage.Recycling && _migrationOperation?.IsCancellationRequested != true;
        BackButton.Content = canKeepSource ? L("保留原目录", "Keep original folder") : importing && _migrationOperation is not null ? L("取消", "Cancel") : L("上一步", "Back");
        NextButton.IsVisible = _page != Page.Data;
        NextButton.IsEnabled = !importing || !_pickerOpen && _migrationOperation is null && (_migrationFinished || _importPlan is not null);
        NextButton.Content = _page switch
        {
            Page.Welcome => L("同意并继续", "Agree and continue"),
            Page.Import when !_migrationFinished => IsPreview ? L("预览迁移", "Preview import") : L("开始迁移", "Start migration"),
            Page.Finish => L("完成", "Finish"),
            _ => L("继续", "Continue"),
        };
        var step = _page <= Page.Data ? (int)_page + 1 : (int)_page;
        StepIndicator.Text = L($"第 {step} 步 / 共 {StepCount} 步", $"Step {step} of {StepCount}");
    }

    private void StartOobeLogin()
    {
        if (IsPreview) return;
        OobeLoginStatus.Text = _isEnglish
            ? "Opening the secure sign-in window…"
            : "正在打开安全登录窗口…";
        App.OpenLoginWindow();
    }

    private void RefreshOobeAccount()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshOobeAccount);
            return;
        }
        if (_closed || _page != Page.Account) return;
        var profile = AccountService.Current;
        var loggedIn = profile is not null;

        OobeAccountCard.IsVisible = loggedIn;
        OobeLogoutButton.IsVisible = loggedIn && !IsPreview;
        OobeLoginButton.IsEnabled = !loggedIn && !IsPreview;

        if (!loggedIn)
        {
            if (!string.IsNullOrEmpty(OobeAccountName.Text)) AccountAvatar.Apply(OobeAvatar, "", null);
            OobeAccountName.Text = null;
            OobeAccountEmail.Text = null;
            OobeLoginStatus.Text = IsPreview
                ? L("向导预览不会更改账户。", "This preview does not change your account.")
                : L("可以在默认浏览器中登录或注册 ClassNEX 账户。", "You can sign in or register in your default browser.");
            return;
        }

        OobeAccountName.Text = profile!.DisplayName;
        OobeAccountEmail.Text = profile.Email;
        OobeLoginStatus.Text = _isEnglish
            ? "Signed in. Your profile has been synced."
            : "登录成功，账号信息已同步。";

        AccountAvatar.Apply(OobeAvatar, profile.DisplayName, AccountService.AvatarImagePath);
    }

    private void SwitchPage(Page target, Button? button = null)
    {
        if (_closed || _finishing || _pickerOpen || _migrationOperation is not null) return;
        var direction = Math.Sign((int)target - (int)_page);
        if (direction == 0 || button is not null && IsRepeatedTap(button)) return;
        CurrentPage().IsVisible = false;
        _page = target;
        CurrentPage().IsVisible = true;
        if (_page == Page.Account) RefreshOobeAccount();
        if (_page == Page.Import) RefreshMigrationPage();
        if (_page == Page.Finish) UpdateFinishMessage();
        UpdateFooter();
        ArmGreeting();

        var animate = MotionEnabled;
        if (animate && ElementComposition.GetElementVisual(PageBody) is { } host)
        {
            var shift = host.Compositor.CreateVector3KeyFrameAnimation();
            var origin = new Vector3((float)PageBody.Bounds.X, (float)PageBody.Bounds.Y, 0);
            shift.Duration = DesktopMotion.Enter;
            shift.InsertKeyFrame(0, origin + new Vector3(12 * direction, 0, 0));
            shift.InsertKeyFrame(1, origin, DesktopMotion.EnterEase);
            host.StartAnimation(nameof(host.Offset), shift);
            FadeIn(host, .8f);
        }
        _lastStepButton = button;
        _lastStep = Stopwatch.GetTimestamp();
    }

    private static void FadeIn(CompositionVisual visual, float initial)
    {
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = DesktopMotion.Enter;
        fade.InsertKeyFrame(0, initial);
        fade.InsertKeyFrame(1, 1, DesktopMotion.EnterEase);
        visual.StartAnimation(nameof(visual.Opacity), fade);
    }

    private Control CurrentPage() => _page switch
    {
        Page.Welcome => PageWelcome,
        Page.Data => PageData,
        Page.Import => PageImport,
        Page.Preferences => PagePreferences,
        Page.Account => PageAccount,
        Page.More => PageMore,
        _ => PageFinish,
    };

    private async Task ShowPrivacyAsync()
    {
        var dialog = new AvaloniaFluentUI.Controls.ContentDialog
        {
            Title = _isEnglish ? "Privacy policy" : "隐私政策",
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 14,
                    Text = _isEnglish
                        ? "You can use the timetable without an account. Timetables, preferences and logs are stored on this device.\n\n"
                          + "Signing in opens your browser and retrieves your profile from ClassNEX ID. If you connect to Central, the client exchanges supported settings and timetable data with that server.\n\n"
                          + "When you send a request to NEX, its configured model provider receives the conversation and any classroom context included in the request. Voice wake-up uses your microphone and can be turned off."
                        : "无需账户也可以使用课表。课表、设置和日志保存在本机。\n\n"
                          + "登录时会打开浏览器，并从 ClassNEX ID 读取账户资料。连接集控后，客户端会与该服务器同步支持的设置和课表。\n\n"
                          + "向 NEX 发起请求时，配置的模型服务会收到对话和请求所附的课堂信息。语音唤醒会使用麦克风，可以关闭。",
                },
            },
            CloseButtonText = _isEnglish ? "Close" : "关闭",
            DefaultButton = AvaloniaFluentUI.Controls.ContentDialogButton.Close,
        };

        await dialog.ShowAsync(this);
    }

    private void UpdateFinishMessage() => FinishBody.Text = _mode switch
    {
        OobeMode.SettingsPreview => L("点击「完成」返回当前桌面，预览中的更改不会保存。", "Select Finish to return to your desktop without saving preview changes."),
        OobeMode.StandalonePreview => L("点击「完成」显示预览课表。此处的选择不影响你的设备。", "Select Finish to open the sample timetable. Your device settings will not change."),
        _ when _migrationFinished && !AppServices.Settings.IsMainWindowVisible => L("课表浮窗将保留隐藏状态。需要时可从托盘菜单显示浮窗。", "The timetable will stay hidden. You can show it from the system tray."),
        _ => L("点击「完成」在桌面上显示课表浮窗。", "Select Finish to show the timetable on your desktop."),
    };

    private void Finish()
    {
        if (_closed || _finishing || IsRepeatedTap(NextButton) || _page != Page.Finish) return;
        _finishing = true;
        if (_mode == OobeMode.SettingsPreview)
        {
            Close();
            return;
        }

        var s = AppServices.Settings;
        var previous = (s.IsOobeCompleted, s.IsMainWindowVisible, s.SemesterStartDate, s.Language, s.ThemeMode, s.RunAtStartup, s.IsNexVoiceWakeEnabled);
        void RestoreDraft() => (s.IsOobeCompleted, s.IsMainWindowVisible, s.SemesterStartDate, s.Language, s.ThemeMode, s.RunAtStartup, s.IsNexVoiceWakeEnabled) = previous;
        if (!IsPreview)
        {
            s.IsOobeCompleted = true;
            if (!_migrationFinished) s.IsMainWindowVisible = true;
            s.SemesterStartDate = SemesterPicker.SelectedDate?.Date;
            s.Language = _isEnglish ? "en-US" : "zh-CN";
            s.ThemeMode = ThemeCombo.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            s.RunAtStartup = RunAtStartupCheck.IsChecked == true;
            s.IsNexVoiceWakeEnabled = VoiceWakeCheck.IsChecked == true;
            try { AppServices.SettingsStore.Save(s); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                Trace.WriteLine(ex);
                RestoreDraft();
                _finishing = false;
                FinishBody.Text = L("无法保存向导设置。请检查数据目录的写入权限或可用空间，再点击「完成」重试。", "Setup could not save your settings. Check write access and free space, then select Finish to retry.");
                return;
            }
        }
        try
        {
            var enterDesktop = SetupCompleted ?? throw new InvalidOperationException("缺少桌面启动入口。");
            enterDesktop();
        }
        catch (Exception ex)
        {
            Trace.TraceError("Unable to enter the classroom: {0}", ex);
            if (!IsPreview)
            {
                RestoreDraft();
                try { AppServices.SettingsStore.Save(s); }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Trace.TraceError("{0}", rollback); }
            }
            _finishing = false;
            FinishBody.Text = L("尚未能打开桌面课表。请再次点击「完成」重试。", "The desktop timetable could not open. Select Finish to try again.");
            return;
        }
        if (!IsPreview && _initialRunAtStartup != s.RunAtStartup) StartupRegistry.SetRunAtStartup(s.RunAtStartup);
        Close();
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            
        }
    }
}

