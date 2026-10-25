using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaFluentUI.Windowing;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class LoginWindow : AppWindow
{
    private DispatcherTimer? _codeCooldownTimer;
    private int _codeCooldownLeft;
    private bool _busy;

    public LoginWindow()
    {
        InitializeComponent();

        ModeLoginButton.Click += (_, _) => SwitchMode(false);
        ModeSignupButton.Click += (_, _) => SwitchMode(true);
        SignInButton.Click += async (_, _) => await SignInAsync();
        SendCodeButton.Click += async (_, _) => await SendCodeAsync();
        SignUpButton.Click += async (_, _) => await SignUpAsync();
        FinishButton.Click += (_, _) => Close();
        LogoutButton.Click += (_, _) => DoLogout();
        CloseButton.Click += (_, _) => Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        try
        {
            if (TitleBar is { } bar)
                bar.TitleBarHitTestType = TitleBarHitTestType.Complex;

            AppWindow.SetAllowInteractionInTitleBar(this, true);
        }
        catch
        {
        }

        try
        {
            if (OperatingSystem.IsWindows() && Environment.OSVersion.Version >= new Version(10, 0, 22000))
            {
                TransparencyLevelHint = new[]
                {
                    WindowTransparencyLevel.Mica,
                    WindowTransparencyLevel.AcrylicBlur,
                    WindowTransparencyLevel.None,
                };
                Background = Brushes.Transparent;
            }
        }
        catch
        {
        }

        RefreshState();
    }

    private void SwitchMode(bool signup)
    {
        LoginForm.IsVisible = !signup;
        SignupForm.IsVisible = signup;
        ModeLoginButton.Classes.Set("Primary", !signup);
        ModeSignupButton.Classes.Set("Primary", signup);
    }

    private void RefreshState()
    {
        var profile = AccountService.Current;
        var loggedIn = profile is not null;

        StateForm.IsVisible = !loggedIn;
        StateDone.IsVisible = false;
        StateLoggedIn.IsVisible = loggedIn;

        if (!loggedIn)
            return;

        CurrentNameText.Text = profile!.DisplayName;
        CurrentEmailText.Text = profile.Email;
        AccountAvatar.Apply(CurrentAvatar, profile.DisplayName, AccountService.AvatarImagePath);
    }

    private async Task SignInAsync()
    {
        if (_busy)
            return;

        var account = LoginAccountBox.Text ?? "";
        var password = LoginPasswordBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            LoginStatusText.Text = "请输入用户名和密码。";
            return;
        }

        _busy = true;
        SignInButton.IsEnabled = false;
        LoginStatusText.Text = "正在登录…";
        try
        {
            var (ok, message) = await AccountService.SignInAsync(account, password);
            if (ok)
            {
                ShowSuccess();
                return;
            }

            LoginStatusText.Text = message;
        }
        finally
        {
            _busy = false;
            SignInButton.IsEnabled = true;
        }
    }

    private async Task SendCodeAsync()
    {
        if (_busy)
            return;

        var email = SignupEmailBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(email))
        {
            SignupStatusText.Text = "请先填写邮箱。";
            return;
        }

        _busy = true;
        SendCodeButton.IsEnabled = false;
        SignupStatusText.Text = "正在发送验证码…";
        try
        {
            var (ok, message) = await AccountService.SendSignupCodeAsync(email);
            if (ok)
            {
                StartCodeCooldown();
                SignupStatusText.Text = "验证码已发送，请查收邮箱。";
            }
            else
            {
                SendCodeButton.IsEnabled = true;
                SignupStatusText.Text = message;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void StartCodeCooldown()
    {
        _codeCooldownLeft = 60;
        SendCodeButton.Content = $"60 秒后重发";
        _codeCooldownTimer?.Stop();
        _codeCooldownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _codeCooldownTimer.Tick += (_, _) =>
        {
            _codeCooldownLeft--;
            if (_codeCooldownLeft <= 0)
            {
                _codeCooldownTimer?.Stop();
                SendCodeButton.IsEnabled = true;
                SendCodeButton.Content = "发送验证码";
                return;
            }

            SendCodeButton.Content = $"{_codeCooldownLeft} 秒后重发";
        };
        _codeCooldownTimer.Start();
    }

    private async Task SignUpAsync()
    {
        if (_busy)
            return;

        var username = (SignupUsernameBox.Text ?? "").Trim();
        var displayName = (SignupDisplayNameBox.Text ?? "").Trim();
        var email = (SignupEmailBox.Text ?? "").Trim();
        var password = SignupPasswordBox.Text ?? "";
        var confirm = SignupConfirmBox.Text ?? "";
        var code = (SignupCodeBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            SignupStatusText.Text = "请填写用户名、邮箱和密码。";
            return;
        }

        if (password != confirm)
        {
            SignupStatusText.Text = "两次输入的密码不一致。";
            return;
        }

        if (code.Length == 0)
        {
            SignupStatusText.Text = "请输入邮箱收到的验证码。";
            return;
        }

        _busy = true;
        SignUpButton.IsEnabled = false;
        SignupStatusText.Text = "正在创建账户…";
        try
        {
            var (ok, message) = await AccountService.SignUpAsync(
                username, displayName, email, password, code);
            if (ok)
            {
                SignupStatusText.Text = "注册成功，正在自动登录…";
                var (signOk, signMessage) = await AccountService.SignInAsync(username, password);
                if (signOk)
                {
                    ShowSuccess();
                    return;
                }

                SignupStatusText.Text = "账户已创建：" + signMessage + "（可切换到登录页直接登录）";
            }
            else
            {
                SignupStatusText.Text = message;
            }
        }
        finally
        {
            _busy = false;
            SignUpButton.IsEnabled = true;
        }
    }

    private void ShowSuccess()
    {
        var profile = AccountService.Current;
        if (profile is null)
            return;

        AppServices.Settings.UserName = profile.DisplayName;
        AppServices.Settings.Email = profile.Email;
        AppServices.SaveSettings();

        DoneNameText.Text = profile.DisplayName;
        DoneEmailText.Text = profile.Email;
        AccountAvatar.Apply(DoneAvatar, profile.DisplayName, AccountService.AvatarImagePath);

        StateForm.IsVisible = false;
        StateDone.IsVisible = true;
        StateLoggedIn.IsVisible = false;
    }

    private void DoLogout()
    {
        AccountService.Logout();
        RefreshState();
    }
}
