using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
        ReopenBrowserButton.Click += (_, _) => OpenBrowser(_loginUrl);
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

    private void RefreshState()
    {
        var profile = AccountService.Current;

        StateIdle.IsVisible = profile is null && !AccountService.FlowActive;
        StateFlow.IsVisible = AccountService.FlowActive;
        StateDone.IsVisible = false;
        StateLoggedIn.IsVisible = profile is not null;

        if (profile is not null)
        {
            CurrentNameText.Text = profile.DisplayName;
            CurrentEmailText.Text = profile.Email;
            ApplyAvatar(CurrentAvatar, profile);
        }
    }

    private async void StartLogin()
    {
        try
        {
            _loginUrl = AccountService.BuildLoginUrl();
            FlowStatusText.Text = "已打开浏览器，请在浏览器中完成登录。";
            StateIdle.IsVisible = false;
            StateFlow.IsVisible = true;
            StateDone.IsVisible = false;
            StateLoggedIn.IsVisible = false;

            OpenBrowser(_loginUrl);

            _pollTimer?.Stop();
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            _pollTimer.Tick += async (_, _) => await PollOnceAsync();
            _pollTimer.Start();
        }
        catch (Exception ex)
        {
            FlowStatusText.Text = $"无法连接账户服务：{ex.Message}";
            StateIdle.IsVisible = true;
            StateFlow.IsVisible = false;
        }
    }

    private async Task PollOnceAsync()
    {
        if (_pollBusy)
            return;

        _pollBusy = true;
        try
        {
            if (!AccountService.TryTakeCode(out var code) || code is null)
                return;

            var done = await AccountService.CompleteLoginAsync(code);
            if (done)
            {
                _pollTimer?.Stop();
                ShowSuccess();
                return;
            }

            FlowStatusText.Text = "浏览器已返回登录结果，但换取登录信息失败，请重试。";
        }
        catch (Exception ex)
        {
            FlowStatusText.Text = $"登录出错：{ex.Message}";
        }
        finally
        {
            _pollBusy = false;
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
        ApplyAvatar(DoneAvatar, profile);

        StateIdle.IsVisible = false;
        StateFlow.IsVisible = false;
        StateDone.IsVisible = true;
        StateLoggedIn.IsVisible = false;
    }

    private void CancelLogin()
    {
        _pollTimer?.Stop();
        AccountService.CancelLogin();
        RefreshState();
    }

    private void DoLogout()
    {
        AccountService.Logout();
        RefreshState();
    }

    private static void ApplyAvatar(Border target, Models.AccountProfile profile)
    {
        try
        {
            Bitmap? bitmap = null;
            var local = AccountService.AvatarImagePath;
            if (local is not null)
            {
                using var stream = File.OpenRead(local);
                bitmap = new Bitmap(stream);
            }
            else if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                _ = AccountService.RefreshAvatarAsync();
            }

            if (bitmap is null)
            {
                using var stream = Avalonia.Platform.AssetLoader.Open(new Uri("avares://ClassNex/Assets/avatar.png"));
                bitmap = new Bitmap(stream);
            }

            target.Background = new ImageBrush(bitmap)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };

            if (CiPalette.TryResource("AccentFillColorDefaultBrush", out var brush) &&
                brush is ISolidColorBrush solid)
                target.BorderBrush = new SolidColorBrush(solid.Color);
        }
        catch
        {
        }
    }

    private static void OpenBrowser(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
