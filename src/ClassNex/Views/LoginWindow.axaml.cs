using System.Diagnostics;
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
    private DispatcherTimer? _pollTimer;
    private string _loginUrl = "";
    private bool _pollBusy;

    public LoginWindow()
    {
        InitializeComponent();

        StartLoginButton.Click += (_, _) => StartLogin();
        ReopenBrowserButton.Click += (_, _) => ReopenBrowser();
        CancelFlowButton.Click += (_, _) => CancelLogin();
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

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer?.Stop();
        AccountService.CancelLogin();
        base.OnClosed(e);
    }

    private void RefreshState()
    {
        var profile = AccountService.Current;

        StateIdle.IsVisible = profile is null;
        StateFlow.IsVisible = false;
        StateDone.IsVisible = false;
        StateLoggedIn.IsVisible = profile is not null;

        if (profile is null)
            return;

        CurrentNameText.Text = profile!.DisplayName;
        CurrentEmailText.Text = profile.Email;
        AccountAvatar.Apply(CurrentAvatar, profile.DisplayName, AccountService.AvatarImagePath);
    }

    private void StartLogin()
    {
        try
        {
            _loginUrl = AccountService.BuildLoginUrl();
            ShowFlow("已打开默认浏览器，请在浏览器中完成登录或注册。");
            if (!OpenBrowser(_loginUrl))
                FlowStatusText.Text = "无法自动打开浏览器，请点击「重新打开浏览器」再试。";
            StartPolling();
        }
        catch (Exception ex)
        {
            ShowFlow("无法连接账户服务：" + ex.Message);
            ReopenBrowserButton.Content = "重试登录";
            CancelFlowButton.Content = "返回";
        }
    }

    private void ReopenBrowser()
    {
        if (!string.IsNullOrWhiteSpace(_loginUrl) && AccountService.FlowActive)
        {
            if (!OpenBrowser(_loginUrl))
                FlowStatusText.Text = "无法打开浏览器，请检查默认浏览器设置后重试。";
            return;
        }

        StartLogin();
    }

    private void StartPolling()
    {
        _pollTimer?.Stop();
        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _pollTimer.Tick += async (_, _) => await PollOnceAsync();
        _pollTimer.Start();
    }

    private async Task PollOnceAsync()
    {
        if (_pollBusy || !AccountService.TryTakeCode(out var code) || code is null)
            return;

        _pollBusy = true;
        FlowStatusText.Text = "已收到浏览器授权，正在同步账户资料…";
        try
        {
            var (ok, message) = await AccountService.CompleteLoginAsync(code);
            if (ok)
            {
                _pollTimer?.Stop();
                ShowSuccess();
                return;
            }

            FlowStatusText.Text = "授权已完成，但账户资料同步失败：" + message;
            ReopenBrowserButton.Content = "重新登录";
        }
        finally
        {
            _pollBusy = false;
        }
    }

    private void ShowFlow(string status)
    {
        StateIdle.IsVisible = false;
        StateFlow.IsVisible = true;
        StateDone.IsVisible = false;
        StateLoggedIn.IsVisible = false;
        FlowStatusText.Text = status;
        ReopenBrowserButton.Content = "重新打开浏览器";
        CancelFlowButton.Content = "取消登录";
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

        StateIdle.IsVisible = false;
        StateFlow.IsVisible = false;
        StateDone.IsVisible = true;
        StateLoggedIn.IsVisible = false;
    }

    private void CancelLogin()
    {
        _pollTimer?.Stop();
        AccountService.CancelLogin();
        _loginUrl = "";
        RefreshState();
    }

    private void DoLogout()
    {
        AccountService.Logout();
        RefreshState();
    }

    private static bool OpenBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
