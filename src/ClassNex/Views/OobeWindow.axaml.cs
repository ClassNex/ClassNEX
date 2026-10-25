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
    private DispatcherTimer? _loginPollTimer;
    private bool _loginPollBusy;
    private readonly List<Avalonia.Controls.Shapes.Ellipse> _dots = new();

    public OobeWindow()
    {
        InitializeComponent();

        
        GreetingText.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(260) },
        };

        BuildDots();
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
            
            if (!AppServices.Settings.IsOobeCompleted)
                App.ExitApp();
        };
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _greetingTimer?.Start();
    }

    private void BuildDots()
    {
        for (var i = 0; i < PageCount; i++)
        {
            var idx = i;
            var dot = new Avalonia.Controls.Shapes.Ellipse { Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) };
            dot.Classes.Add("PageDot");
            dot.PointerPressed += (_, _) => SwitchPage(idx);
            _dots.Add(dot);
            PageDots.Children.Add(dot);
        }
    }

    private void WireEvents()
    {
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

        PrivacyLink.Click += async (_, _) => await ShowPrivacyAsync();
        LicenseLink.Click += (_, _) => OpenUrl("https://github.com/ClassNex/ClassNEX/blob/main/LICENSE");

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
            AccountBody.Text = "After signing in, your avatar, name and email are synced from the account service.";
            OobeLoginButton.Content = "Sign in / Sign up";
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
            AccountBody.Text = "登录后，头像、用户名与邮箱会从账户服务同步到本机。";
            OobeLoginButton.Content = "登录 / 注册 ClassNEX 账号";
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
    }

    private async void StartOobeLogin()
    {
        try
        {
            var loginUrl = AccountService.BuildLoginUrl();
            OobeLoginStatus.Text = _isEnglish
                ? "A browser window has been opened for you to finish signing in."
                : "已打开浏览器，请在浏览器中完成登录。";
            OobeLoginButton.IsEnabled = false;

            OpenUrl(loginUrl);

            _loginPollTimer?.Stop();
            _loginPollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _loginPollTimer.Tick += async (_, _) => await PollOobeLoginAsync();
            _loginPollTimer.Start();
        }
        catch (Exception ex)
        {
            OobeLoginStatus.Text = (_isEnglish ? "Could not reach the account service: " : "无法连接账户服务：") + ex.Message;
            OobeLoginButton.IsEnabled = true;
        }
    }

    private async Task PollOobeLoginAsync()
    {
        if (_loginPollBusy)
            return;

        _loginPollBusy = true;
        try
        {
            if (!AccountService.TryTakeCode(out var code) || code is null)
                return;

            var done = await AccountService.CompleteLoginAsync(code);
            if (done)
            {
                _loginPollTimer?.Stop();
                RefreshOobeAccount();
                OobeLoginStatus.Text = _isEnglish ? "Signed in. Your profile has been synced." : "登录成功，账号信息已同步。";
                OobeLoginButton.IsEnabled = false;
                return;
            }

            OobeLoginStatus.Text = _isEnglish
                ? "The browser returned the sign-in result, but syncing the profile failed. Please try again."
                : "浏览器已返回登录结果，但同步登录信息失败，请重试。";
            _loginPollTimer?.Stop();
            OobeLoginButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            OobeLoginStatus.Text = (_isEnglish ? "Sign-in error: " : "登录出错：") + ex.Message;
        }
        finally
        {
            _loginPollBusy = false;
        }
    }

    private void RefreshOobeAccount()
    {
        var profile = AccountService.Current;
        var loggedIn = profile is not null;

        OobeAccountCard.IsVisible = loggedIn;
        OobeLogoutButton.IsVisible = loggedIn;
        OobeLoginButton.IsEnabled = !loggedIn && !AccountService.FlowActive;

        if (!loggedIn)
            return;

        OobeAccountName.Text = profile!.DisplayName;
        OobeAccountEmail.Text = profile.Email;

        try
        {
            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            if (AccountService.AvatarImagePath is { } local)
            {
                using var stream = File.OpenRead(local);
                bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }

            if (bitmap is null)
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(
                    new Uri("avares://ClassNex/Assets/avatar.png"));
                bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            }

            OobeAvatar.Background = new Avalonia.Media.ImageBrush(bitmap)
            {
                Stretch = Avalonia.Media.Stretch.UniformToFill,
                AlignmentX = Avalonia.Media.AlignmentX.Center,
                AlignmentY = Avalonia.Media.AlignmentY.Center,
            };

            if (CiPalette.TryResource("AccentFillColorDefaultBrush", out var brush) &&
                brush is Avalonia.Media.ISolidColorBrush solid)
                OobeAvatar.BorderBrush = new Avalonia.Media.SolidColorBrush(solid.Color);
        }
        catch
        {
        }
    }

    private void SwitchPage(int page)
    {
        _page = Math.Clamp(page, 0, PageCount - 1);

        PageWelcome.IsVisible = _page == 0;
        PagePreferences.IsVisible = _page == 1;
        PageAccount.IsVisible = _page == 2;
        PageMore.IsVisible = _page == 3;
        PageFinish.IsVisible = _page == 4;

        BackButton.IsVisible = _page > 0;

        for (var i = 0; i < _dots.Count; i++)
        {
            if (i == _page)
                _dots[i].Classes.Add("Active");
            else
                _dots[i].Classes.Remove("Active");
        }

        ApplyLanguage();

        if (_page == PageCount - 1)
            Fireworks.Start();
        else
            Fireworks.Stop();
    }

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
                          + "1. 读写的数据：你导入的课表文件、应用设置、日志，以及（可选的）桌面快捷方式。"
                          + "全部保存在本机的应用数据目录里。\n\n"
                          + "2. 不会收集的数据：没有账号体系、没有遥测、没有统计上报、不会把崩溃日志发到任何地方。"
                          + "除你主动点击外部链接外，应用不访问网络。\n\n"
                          + "3. 网络行为：不上传任何内容。向导里的链接只会用系统浏览器打开。\n\n"
                          + "4. 删除数据：直接删除应用数据目录即可清除全部痕迹。",
                },
            },
            PrimaryButtonText = _isEnglish ? "View full policy on GitHub" : "在 GitHub 上查看完整政策",
            CloseButtonText = _isEnglish ? "Close" : "关闭",
            DefaultButton = AvaloniaFluentUI.Controls.ContentDialogButton.Close,
        };

        var result = await dialog.ShowAsync(this);
        if (result == AvaloniaFluentUI.Controls.ContentDialogResult.Primary)
            OpenUrl("https://github.com/ClassNex/ClassNEX/blob/main/PRIVACY.md");
    }

    private void FinishAsync()
    {
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
