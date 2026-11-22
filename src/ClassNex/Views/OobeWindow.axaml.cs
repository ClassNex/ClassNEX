using System.Diagnostics;
using System.Numerics;
using Avalonia.Animation.Easings;
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
using Windows.UI.ViewManagement;

namespace ClassNex.Views;

public partial class OobeWindow : AppWindow
{
    private const int PageCount = 5;
    private static readonly TimeSpan PageMotion = TimeSpan.FromMilliseconds(360);
    private static readonly CubicEaseOut EaseOut = new();
    private static readonly string[] Greetings =
    [
        "你好", "Hello", "こんにちは", "안녕하세요", "Bonjour", "Hola", "Hallo", "Привет",
        "Ciao", "Olá", "Xin chào", "สวัสดี", "مرحبا", "नमस्ते", "Merhaba", "Hej",
    ];

    private readonly bool _preview;
    private readonly UISettings _systemUi = new();
    private readonly DispatcherTimer _greetingTimer = new() { Interval = TimeSpan.FromMilliseconds(1400) };
    private readonly DispatcherTimer _stepGate = new();
    private CancellationTokenSource? _artworkLoad;
    private Bitmap? _artwork;
    private int _page, _greetingIndex, _greetingEpoch, _lastDirection;
    private bool _isEnglish, _pageSwitching, _finishing, _closed;
    internal bool IsNavigationSettled => !_pageSwitching;
#if DEBUG
    private bool _suppressProbeMotion;
    partial void SuppressMotionForProbe(ref bool suppressed)
    {
        _suppressProbeMotion = true;
        suppressed = !MotionEnabled;
    }
#endif

    private bool MotionEnabled => _systemUi.AnimationsEnabled
#if DEBUG
        && !_suppressProbeMotion
#endif
        ;

    public OobeWindow() : this(false) { }

    public OobeWindow(bool preview)
    {
        _preview = preview;
        InitializeComponent();
        if (preview) Topmost = true;
        LoadCurrentSettings();
        WireEvents();
        _greetingTimer.Tick += (_, _) => RotateGreeting();
        _stepGate.Tick += (_, _) => { _stepGate.Stop(); _pageSwitching = false; };
#if DEBUG
        if (int.TryParse(Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_OOBE"), out var target))
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

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        ++_greetingEpoch;
        _greetingTimer.Stop();
        _stepGate.Stop();
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
        if (_closed || _page != 0) return;
        _greetingIndex = 0;
        GreetingText.Text = Greetings[0];
        RequestAnimationFrame(frameTime =>
        {
            if (_closed || !IsVisible || _page != 0 || epoch != _greetingEpoch) return;
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
        if (_closed || _page != 0 || !MotionEnabled)
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
        NextButton.Click += (_, _) =>
        {
            if (_page == PageCount - 1)
            {
                Finish();
                return;
            }

            SwitchPage(_page + 1);
        };

        BackButton.Click += (_, _) => SwitchPage(_page - 1);

        LanguageCombo.SelectionChanged += (_, _) =>
        {
            _isEnglish = LanguageCombo.SelectedIndex == 1;
            ApplyLanguage();
            RefreshOobeAccount();
        };

        PrivacyLink.Click += async (_, _) => await ShowPrivacyAsync();
        LicenseLink.Click += (_, _) => OpenUrl("https://github.com/ClassNex/ClassNEX/blob/main/LICENSE");

        OobeLoginButton.Click += (_, _) => StartOobeLogin();
        OobeLogoutButton.Click += (_, _) => AccountService.Logout();

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
            FinishBody.Text = "ClassNEX will restart and show the timetable widget on your desktop.";
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
            FinishBody.Text = "点击「完成」后 ClassNEX 会重新启动，并在桌面上显示课表浮窗。";
            BackButton.Content = "上一步";
        }
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        BackButton.IsVisible = _page > 0;
        NextButton.Content = _page switch
        {
            0 => _isEnglish ? "Agree and continue" : "同意并继续",
            PageCount - 1 => _isEnglish ? "Finish" : "完成",
            _ => _isEnglish ? "Continue" : "继续",
        };
        StepIndicator.Text = _isEnglish
            ? $"Step {_page + 1} of {PageCount}"
            : $"第 {_page + 1} 步 / 共 {PageCount} 步";
    }

    private void StartOobeLogin()
    {
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
        if (_closed || _page != 2) return;
        var profile = AccountService.Current;
        var loggedIn = profile is not null;

        OobeAccountCard.IsVisible = loggedIn;
        OobeLogoutButton.IsVisible = loggedIn;
        OobeLoginButton.IsEnabled = !loggedIn;

        if (!loggedIn)
        {
            if (!string.IsNullOrEmpty(OobeAccountName.Text)) AccountAvatar.Apply(OobeAvatar, "", null);
            OobeAccountName.Text = null;
            OobeAccountEmail.Text = null;
            OobeLoginStatus.Text = _isEnglish
                ? "You can sign in or register in your default browser."
                : "可以在默认浏览器中登录或注册 ClassNEX 账户。";
            return;
        }

        OobeAccountName.Text = profile!.DisplayName;
        OobeAccountEmail.Text = profile.Email;
        OobeLoginStatus.Text = _isEnglish
            ? "Signed in. Your profile has been synced."
            : "登录成功，账号信息已同步。";

        AccountAvatar.Apply(OobeAvatar, profile.DisplayName, AccountService.AvatarImagePath);
    }

    private void SwitchPage(int page)
    {
        if (_closed || _finishing) return;
        var target = Math.Clamp(page, 0, PageCount - 1);
        var direction = Math.Sign(target - _page);
        if (direction == 0 || (_pageSwitching && direction == _lastDirection)) return;

        CurrentPage().IsVisible = false;
        _page = target;
        CurrentPage().IsVisible = true;
        if (_page == 2) RefreshOobeAccount();
        UpdateFooter();
        ArmGreeting();

        var animate = MotionEnabled;
        if (animate && ElementComposition.GetElementVisual(PageBody) is { } host)
        {
            var shift = host.Compositor.CreateVector3KeyFrameAnimation();
            var origin = new Vector3((float)PageBody.Bounds.X, (float)PageBody.Bounds.Y, 0);
            shift.Duration = PageMotion;
            shift.InsertKeyFrame(0, origin + new Vector3(18 * direction, 0, 0));
            shift.InsertKeyFrame(1, origin, EaseOut);
            host.StartAnimation(nameof(host.Offset), shift);
            FadeIn(host, .8f);
        }

        _lastDirection = direction;
        _pageSwitching = true;
        _stepGate.Stop();
        _stepGate.Interval = TimeSpan.FromMilliseconds(animate ? 240 : 135);
        _stepGate.Start();
    }

    private static void FadeIn(CompositionVisual visual, float initial)
    {
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = TimeSpan.FromMilliseconds(200);
        fade.InsertKeyFrame(0, initial);
        fade.InsertKeyFrame(1, 1, EaseOut);
        visual.StartAnimation(nameof(visual.Opacity), fade);
    }

    private Control CurrentPage() => _page switch
    {
        0 => PageWelcome,
        1 => PagePreferences,
        2 => PageAccount,
        3 => PageMore,
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

    private void Finish()
    {
        if (_closed || _finishing || _pageSwitching || _page != PageCount - 1) return;
        _finishing = true;
        if (_preview)
        {
            Close();
            return;
        }

        var s = AppServices.Settings;
        s.IsOobeCompleted = true;
        s.SemesterStartDate = SemesterPicker.SelectedDate?.Date;
        s.Language = _isEnglish ? "en-US" : "zh-CN";
        s.ThemeMode = ThemeCombo.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
        var runAtStartup = RunAtStartupCheck.IsChecked == true;
        if (s.RunAtStartup != runAtStartup)
        {
            s.RunAtStartup = runAtStartup;
            StartupRegistry.SetRunAtStartup(runAtStartup);
        }
        s.IsNexVoiceWakeEnabled = VoiceWakeCheck.IsChecked == true;
        s.IsMainWindowVisible = true;
        Services.SettingsService.Save(s);

        App.RestartApplication();
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

