using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaFluentUI.Windowing;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;






public partial class OobeWindow : AppWindow
{
    private const int PageCount = 5;
    private readonly bool _preview;

    private int _page;
    private bool _isEnglish;

    
    private static readonly string[] Greetings =
    {
        "你好", "Hello", "こんにちは", "안녕하세요", "Bonjour", "Hola",
        "Hallo", "Привет", "Ciao", "Olá", "Xin chào", "สวัสดี", "مرحبا", "नमस्ते", "Merhaba", "Hej",
    };

    private int _greetingIndex;
    private DispatcherTimer? _greetingTimer;
    private bool _greetingBusy;
    private bool _assemblyPlayed;
    private bool _pageInitialized;
    private bool _pageSwitching;
    private DispatcherTimer? _pageTurnTimer;
    private readonly Stopwatch _pageTurnClock = new();
    private Control? _turnFrom;
    private Control? _turnTo;
    private bool _turnForward;

    public OobeWindow()
        : this(false)
    {
    }

    public OobeWindow(bool preview)
    {
        _preview = preview;
        InitializeComponent();

        if (_preview)
            Topmost = true;

        GreetingText.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) },
        };

        WireEvents();
        LoadCurrentSettings();
        SwitchPage(0);

        _greetingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1400) };
        _greetingTimer.Tick += async (_, _) => await NextGreetingAsync();

#if DEBUG
        
        if (int.TryParse(Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_OOBE"), out var verifyPage))
            Dispatcher.UIThread.Post(() => SwitchPage(verifyPage), DispatcherPriority.Background);
#endif

        Closing += (_, _) =>
        {
            StopAssemblyAnimation();
            StopPageTurn();

            if (!_preview && !AppServices.Settings.IsOobeCompleted)
                App.ExitApp();
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _greetingTimer?.Start();
        DispatcherTimer.RunOnce(PlayAssemblyAnimation, TimeSpan.FromMilliseconds(450));
    }

    private sealed class FlightGroup
    {
        public Control Control = null!;
        public TranslateTransform Translate = null!;
        public ScaleTransform Scale = null!;
        public RotateTransform Rotate = null!;
        public double Dx;
        public double Dy;
        public double StartAngle;
        public double Delay;
    }

    private DispatcherTimer? _assemblyTimer;
    private readonly Stopwatch _assemblyClock = new();
    private readonly List<FlightGroup> _flights = new();

    private void PlayAssemblyAnimation()
    {
        if (_assemblyPlayed || !IsVisible)
            return;

        _assemblyPlayed = true;
        _flights.Clear();

        var groups = new[]
        {
            (Control: (Control)WelcomeTextPanel, Top: true, Left: true, Delay: 0.0),
            (Control: (Control)WelcomeImage, Top: true, Left: false, Delay: 70.0),
            (Control: (Control)FooterActions, Top: false, Left: false, Delay: 160.0),
        };

        foreach (var group in groups)
        {
            var control = group.Control;
            if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            {
                control.Opacity = 1;
                continue;
            }

            var center = control.TranslatePoint(
                new Point(control.Bounds.Width / 2, control.Bounds.Height / 2),
                this);

            if (center is not { } finalCenter)
            {
                control.Opacity = 1;
                continue;
            }

            var cornerX = group.Left ? 0 : Bounds.Width;
            var cornerY = group.Top ? 0 : Bounds.Height;
            var dx = cornerX - finalCenter.X;
            var dy = cornerY - finalCenter.Y;
            var startAngle = group.Left ? -6.0 : 6.0;

            var translate = new TranslateTransform { X = dx, Y = dy };
            var scale = new ScaleTransform { ScaleX = 0.78, ScaleY = 0.78 };
            var rotate = new RotateTransform { Angle = startAngle };
            var transform = new TransformGroup();
            transform.Children.Add(translate);
            transform.Children.Add(scale);
            transform.Children.Add(rotate);

            control.RenderTransformOrigin = RelativePoint.Center;
            control.RenderTransform = transform;
            control.Opacity = 0;

            _flights.Add(new FlightGroup
            {
                Control = control,
                Translate = translate,
                Scale = scale,
                Rotate = rotate,
                Dx = dx,
                Dy = dy,
                StartAngle = startAngle,
                Delay = group.Delay,
            });
        }

        if (_flights.Count == 0)
            return;

        _assemblyClock.Restart();
        _assemblyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _assemblyTimer.Tick += OnAssemblyTick;
        _assemblyTimer.Start();
    }

    private void OnAssemblyTick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            StopAssemblyAnimation();
            return;
        }

        const double duration = 780.0;
        var elapsed = _assemblyClock.Elapsed.TotalMilliseconds;
        var allDone = true;

        foreach (var flight in _flights)
        {
            var progress = Math.Clamp((elapsed - flight.Delay) / duration, 0.0, 1.0);
            if (progress < 1.0)
                allDone = false;

            var eased = 1.0 - Math.Pow(1.0 - progress, 3.0);
            var overshoot = Math.Sin(progress * Math.PI) * 0.035;

            flight.Translate.X = flight.Dx * (1.0 - eased);
            flight.Translate.Y = flight.Dy * (1.0 - eased);
            flight.Scale.ScaleX = 0.78 + 0.22 * eased + overshoot;
            flight.Scale.ScaleY = 0.78 + 0.22 * eased + overshoot;
            flight.Rotate.Angle = flight.StartAngle * (1.0 - eased);
            flight.Control.Opacity = Math.Min(1.0, progress * 1.7);

            if (progress >= 1.0)
            {
                flight.Translate.X = 0;
                flight.Translate.Y = 0;
                flight.Scale.ScaleX = 1;
                flight.Scale.ScaleY = 1;
                flight.Rotate.Angle = 0;
                flight.Control.Opacity = 1;
            }
        }

        if (allDone)
            StopAssemblyAnimation();
    }

    private void StopAssemblyAnimation()
    {
        _assemblyTimer?.Stop();
        _assemblyTimer = null;
    }

    private void WireEvents()
    {
        ClassNex.Styles.MetroTilt.Attach(BackButton);
        ClassNex.Styles.MetroTilt.Attach(NextButton);

        NextButton.Click += (_, _) =>
        {
            if (_page == PageCount - 1)
            {
                FinishAsync();
                return;
            }

            SwitchPage(_page + 1);
        };

        BackButton.Click += (_, _) => SwitchPage(_page - 1);

        LanguageCombo.SelectionChanged += (_, _) =>
        {
            _isEnglish = LanguageCombo.SelectedIndex == 1;
            AppServices.Settings.Language = _isEnglish ? "en-US" : "zh-CN";
            ApplyLanguage();
        };

        PrivacyLink.Click += async (_, _) => await ShowPrivacyAsync();        LicenseLink.Click += (_, _) => OpenUrl("https://github.com/ClassNex/ClassNEX/blob/main/LICENSE");

        OobeLoginButton.Click += (_, _) => StartOobeLogin();
        OobeLogoutButton.Click += (_, _) =>
        {
            AccountService.Logout();
            RefreshOobeAccount();
        };

        AccountService.Changed += RefreshOobeAccount;

        ThemeCombo.SelectionChanged += (_, _) =>
        {
            var mode = ThemeCombo.SelectedIndex switch
            {
                1 => "light",
                2 => "dark",
                _ => "system",
            };

            AppServices.Settings.ThemeMode = mode;
            App.ApplyTheme(mode);
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

        ApplyLanguage();
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
            RunAtStartupCheck.Content = "Start ClassNEX when Windows starts";
            AccountTitle.Text = "Account";
            AccountBody.Text = "Sign in or create an account securely in your browser. Your avatar, name and email are synced automatically.";
            OobeLoginButton.Content = "Sign in / Sign up in browser";
            OobeLogoutButton.Content = "Sign out";
            MoreTitle.Text = "More settings";
            MoreBody.Text = "Reserved: more options will be added in a later version.";
            FinishTitle.Text = "You're all set";
            FinishBody.Text = "ClassNEX will restart and show the timetable widget on your desktop.";
            BackButton.Content = "Back";
            NextButton.Content = _page switch
            {
                0 => "Agree and continue",
                PageCount - 1 => "Finish",
                _ => "Continue",
            };
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
            RunAtStartupCheck.Content = "开机时自动启动 ClassNEX";
            AccountTitle.Text = "账户";
            AccountBody.Text = "在浏览器中安全登录或注册，头像、用户名与邮箱会自动同步到本机。";
            OobeLoginButton.Content = "使用浏览器登录 / 注册";
            OobeLogoutButton.Content = "退出登录";
            MoreTitle.Text = "更多设置";
            MoreBody.Text = "此页面预留：后续版本继续补充。";
            FinishTitle.Text = "一切就绪";
            FinishBody.Text = "点击「完成」后 ClassNEX 会重新启动，并在桌面上显示课表浮窗。";
            BackButton.Content = "上一步";
            NextButton.Content = _page switch
            {
                0 => "同意并继续",
                PageCount - 1 => "完成",
                _ => "继续",
            };
        }

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
        var profile = AccountService.Current;
        var loggedIn = profile is not null;

        OobeAccountCard.IsVisible = loggedIn;
        OobeLogoutButton.IsVisible = loggedIn;
        OobeLoginButton.IsEnabled = !loggedIn;

        if (!loggedIn)
        {
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

    private ScaleTransform? _fromScale;
    private ScaleTransform? _toScale;
    private SkewTransform? _fromSkew;
    private SkewTransform? _toSkew;
    private TranslateTransform? _fromShift;
    private TranslateTransform? _toShift;

    private void SwitchPage(int page)
    {
        if (_pageSwitching)
            return;

        var target = Math.Clamp(page, 0, PageCount - 1);
        if (target == _page && _pageInitialized)
            return;

        var previous = _page;
        var hadPrevious = _pageInitialized;
        var from = CurrentPage();

        _page = target;
        var to = CurrentPage();

        foreach (var candidate in new[] { PageWelcome, PagePreferences, PageAccount, PageMore, PageFinish })
            candidate.IsVisible = false;

        if (hadPrevious && previous != target)
        {
            from.IsVisible = true;
            to.IsVisible = true;
            StartPageTurn(from, to, target > previous);
        }
        else
        {
            to.IsVisible = true;
        }

        _pageInitialized = true;
        BackButton.IsVisible = _page > 0;

        ApplyLanguage();

        if (_page == PageCount - 1)
            Fireworks.Start();
        else
            Fireworks.Stop();
    }

    private void StartPageTurn(Control from, Control to, bool forward)
    {
        StopPageTurn();

        _pageSwitching = true;
        _turnFrom = from;
        _turnTo = to;
        _turnForward = forward;

        _fromScale = new ScaleTransform(1, 1);
        _toScale = new ScaleTransform(0.06, 1);
        _fromSkew = new SkewTransform(0, 0);
        _toSkew = new SkewTransform(forward ? 5 : -5, 0);
        _fromShift = new TranslateTransform();
        _toShift = new TranslateTransform(forward ? 42 : -42, 0);

        from.RenderTransformOrigin = new RelativePoint(forward ? -0.10 : 1.10, 0.5, RelativeUnit.Relative);
        to.RenderTransformOrigin = new RelativePoint(forward ? -0.10 : 1.10, 0.5, RelativeUnit.Relative);

        from.RenderTransform = new TransformGroup
        {
            Children = { _fromScale, _fromSkew, _fromShift },
        };
        to.RenderTransform = new TransformGroup
        {
            Children = { _toScale, _toSkew, _toShift },
        };

        from.Opacity = 1;
        to.Opacity = 0;
        from.ZIndex = 1;
        to.ZIndex = 2;

        _pageTurnClock.Restart();
        _pageTurnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pageTurnTimer.Tick += OnPageTurnTick;
        _pageTurnTimer.Start();
    }

    private void OnPageTurnTick(object? sender, EventArgs e)
    {
        if (_turnFrom is null || _turnTo is null)
        {
            StopPageTurn();
            return;
        }

        const double duration = 360.0;
        var progress = Math.Clamp(_pageTurnClock.Elapsed.TotalMilliseconds / duration, 0, 1);
        var eased = MetroEase(progress);

        var direction = _turnForward ? 1 : -1;
        var collapsed = 0.06;

        if (_fromScale is not null && _toScale is not null &&
            _fromSkew is not null && _toSkew is not null &&
            _fromShift is not null && _toShift is not null)
        {
            _fromScale.ScaleX = 1 - (1 - collapsed) * eased;
            _toScale.ScaleX = collapsed + (1 - collapsed) * eased;

            _fromSkew.AngleX = -5 * direction * eased;
            _toSkew.AngleX = 5 * direction * (1 - eased);

            _fromShift.X = -18 * direction * eased;
            _toShift.X = 42 * direction * (1 - eased);

            _turnFrom.Opacity = 1 - eased;
            _turnTo.Opacity = eased;
        }

        if (progress < 1)
            return;

        _turnFrom.IsVisible = false;
        _turnTo.IsVisible = true;
        _turnTo.Opacity = 1;
        _turnTo.RenderTransform = null;
        _turnTo.RenderTransformOrigin = RelativePoint.Center;
        _turnFrom.RenderTransform = null;
        _turnFrom.RenderTransformOrigin = RelativePoint.Center;

        _turnFrom.ZIndex = 0;
        _turnTo.ZIndex = 0;

        StopPageTurn();
    }

    private void StopPageTurn()
    {
        _pageTurnTimer?.Stop();
        _pageTurnTimer = null;
        _pageSwitching = false;
        _turnFrom = null;
        _turnTo = null;
    }

    private static double MetroEase(double t)
    {
        // cubic-bezier(0.1, 0.9, 0.2, 1)
        const double x1 = 0.1, y1 = 0.9, x2 = 0.2, y2 = 1.0;
        var low = 0.0;
        var high = 1.0;
        var u = t;

        for (var i = 0; i < 10; i++)
        {
            var x = 3 * (1 - u) * (1 - u) * u * x1
                    + 3 * (1 - u) * u * u * x2
                    + u * u * u;
            if (x < t)
                low = u;
            else
                high = u;
            u = (low + high) / 2;
        }

        return 3 * (1 - u) * (1 - u) * u * y1
               + 3 * (1 - u) * u * u * y2
               + u * u * u;
    }

    private Control CurrentPage() => _page switch
    {
        0 => PageWelcome,
        1 => PagePreferences,
        2 => PageAccount,
        3 => PageMore,
        _ => PageFinish,
    };

    private async Task NextGreetingAsync()
    {
        if (_greetingBusy || !PageWelcome.IsVisible)
            return;

        _greetingBusy = true;
        try
        {
            GreetingText.Opacity = 0;
            await Task.Delay(240);
            _greetingIndex = (_greetingIndex + 1) % Greetings.Length;
            GreetingText.Text = Greetings[_greetingIndex];
            GreetingText.Opacity = 1;
        }
        finally
        {
            _greetingBusy = false;
        }
    }

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
                        ? "ClassNEX is a local-only classroom timetable display.\n\n"
                          + "1. Data we read and write: the timetable file you import, app settings, logs, and (optionally) a desktop shortcut. "
                          + "All of it stays in the app's data folder on this device.\n\n"
                          + "2. Data we never collect: no account, no telemetry, no analytics, no crash reports sent anywhere. "
                          + "The app does not connect to the internet except when you click an external link.\n\n"
                          + "3. Network: nothing is uploaded. Opening a link in this wizard only opens your browser.\n\n"
                          + "4. Removing data: delete the app's data folder to erase every trace."
                        : "ClassNEX 是一个完全本地运行的教室课表展示工具。\n\n"
                          + "1. 读写的数据：你导入的课表文件、应用设置和日志，以及（可选的）桌面快捷方式。"
                          + "全部保存在本机的应用数据目录里。\n\n"
                          + "2. 不会收集的数据：没有账号体系、没有遥测、没有统计上报，不会把崩溃日志发到任何地方。"
                          + "除你主动点击外部链接外，应用不访问网络。\n\n"
                          + "3. 网络行为：不上传任何内容。向导里的链接只会用系统浏览器打开。\n\n"
                          + "4. 删除数据：直接删除应用数据目录即可清除全部痕迹。",
                },
            },
            PrimaryButtonText = _isEnglish ? "View full policy on GitHub" : "在 GitHub 上查看完整政策",
            DefaultButton = AvaloniaFluentUI.Controls.ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync(this);
        if (result == AvaloniaFluentUI.Controls.ContentDialogResult.Primary)
            OpenUrl("https://github.com/ClassNex/ClassNEX/blob/main/PRIVACY.md");
    }

    private void FinishAsync()
    {
        if (_preview)
        {
            _greetingTimer?.Stop();
            Fireworks.Stop();
            Close();
            return;
        }

        var s = AppServices.Settings;
        s.IsOobeCompleted = true;
        s.SemesterStartDate = SemesterPicker.SelectedDate?.Date;
        s.RunAtStartup = RunAtStartupCheck.IsChecked == true;
        s.IsMainWindowVisible = true;      
        Services.SettingsService.Save(s);

        _greetingTimer?.Stop();
        Fireworks.Stop();

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

