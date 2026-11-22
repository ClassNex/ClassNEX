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
    private readonly Func<string, bool> _openBrowser;
    private int _attempt, _pollingAttempt = -1;

    public LoginWindow() : this(OpenBrowser) { }

    internal LoginWindow(Func<string, bool> openBrowser)
    {
        _openBrowser = openBrowser;
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

        AccountService.Changed += OnAccountChanged;
        RefreshState();
    }

    protected override void OnClosed(EventArgs e)
    {
        AccountService.Changed -= OnAccountChanged;
        _attempt++;
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

        ShowProfile();
    }

    private void ShowProfile()
    {
        var profile = AccountService.Current;
        var name = profile?.DisplayName ?? "";
        var email = profile?.Email ?? "";
        CurrentNameText.Text = DoneNameText.Text = name;
        CurrentEmailText.Text = DoneEmailText.Text = email;
        CurrentEmailText.IsVisible = DoneEmailText.IsVisible = !string.IsNullOrWhiteSpace(email);
        var path = AccountService.AvatarImagePath;
        if (StateLoggedIn.IsVisible || profile is null) AccountAvatar.Apply(CurrentAvatar, name, path);
        if (StateDone.IsVisible || profile is null) AccountAvatar.Apply(DoneAvatar, name, path);
    }

    private void OnAccountChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(OnAccountChanged); return; }
        if (!IsVisible || StateFlow.IsVisible) return;
        if (StateDone.IsVisible && AccountService.Current is not null) ShowProfile();
        else RefreshState();
    }

    internal void BeginAccountSwitch()
    {
        if (AccountService.Current is not null && !StateFlow.IsVisible) StartLogin();
    }

    private void StartLogin()
    {
        _attempt++;
        try
        {
            _loginUrl = AccountService.BuildLoginUrl();
            ShowFlow("已打开默认浏览器，请在浏览器中完成登录或注册。");
            if (!_openBrowser(_loginUrl))
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
            if (!_openBrowser(_loginUrl))
                FlowStatusText.Text = "无法打开浏览器，请检查默认浏览器设置后重试。";
            return;
        }

        StartLogin();
    }

    private void StartPolling()
    {
        if (_pollTimer is null)
        {
            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _pollTimer.Tick += async (_, _) => await PollOnceAsync();
        }
        _pollTimer.Start();
    }

    private async Task PollOnceAsync()
    {
        var attempt = _attempt;
        if (_pollingAttempt == attempt || !AccountService.TryTakeCode(out var code) || code is null)
            return;

        _pollingAttempt = attempt;
        FlowStatusText.Text = "已收到浏览器授权，正在同步账户资料…";
        try
        {
            var (ok, message) = await AccountService.CompleteLoginAsync(code);
            if (attempt != _attempt) return;
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
            if (_pollingAttempt == attempt) _pollingAttempt = -1;
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
        StateIdle.IsVisible = false;
        StateFlow.IsVisible = false;
        StateDone.IsVisible = true;
        StateLoggedIn.IsVisible = false;
        ShowProfile();
    }

    private void CancelLogin()
    {
        _attempt++;
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
