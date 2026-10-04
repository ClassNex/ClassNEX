using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ClassNex.Services;

namespace ClassNex.Views;

/// <summary>
/// 主界面：悬浮在桌面上的课表卡片（对应 ClassIsland 的「主界面」）。
/// 无边框、半透明、置顶、可拖拽。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();

        ApplySettings();
        UpdateContent();

        AppServices.DocumentChanged += UpdateContent;
        AppServices.SettingsChanged += ApplySettings;

        // 每秒刷新，用于实时倒计时
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateContent();
        _timer.Start();

        RootCard.PointerPressed += OnCardPointerPressed;
        RootCard.PointerReleased += OnCardPointerReleased;
    }

    /// <summary>应用设置变化时刷新外观。</summary>
    public void ApplySettings()
    {
        var s = AppServices.Settings;

        Topmost = s.Topmost;
        RootCard.Background = new SolidColorBrush(Colors.Black, Math.Clamp(s.BackgroundOpacity, 0.05, 1.0));
        RootCard.Cursor = new Cursor(StandardCursorType.SizeAll);

        DateText.IsVisible = s.ShowDate;

        var scale = Math.Clamp(s.FontScale, 0.6, 2.0);
        DateText.FontSize = 22 * scale;
        InfoText.FontSize = 20 * scale;

        Position = new PixelPoint((int)s.MainWindowLeft, (int)s.MainWindowTop);
    }

    private void UpdateContent()
    {
        var (date, info) = ScheduleCalculator.Describe(AppServices.Settings, AppServices.Document, DateTime.Now);
        DateText.Text = date;
        InfoText.Text = info;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ClampToScreen();
    }

    /// <summary>确保卡片不会被拖到屏幕外。</summary>
    private void ClampToScreen()
    {
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null)
            return;

        var wa = screen.WorkingArea;
        var x = Math.Clamp(Position.X, wa.X, Math.Max(wa.X, wa.X + wa.Width - 80));
        var y = Math.Clamp(Position.Y, wa.Y, Math.Max(wa.Y, wa.Y + wa.Height - 60));
        Position = new PixelPoint(x, y);
        SavePosition();
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        BeginMoveDrag(e);
        ClampToScreen();
    }

    private void OnCardPointerReleased(object? sender, PointerReleasedEventArgs e) => ClampToScreen();

    private void SavePosition()
    {
        var s = AppServices.Settings;
        s.MainWindowLeft = Position.X;
        s.MainWindowTop = Position.Y;
        AppServices.SaveSettings();
    }

    // ---------- 右键菜单 ----------

    private void OnMenuToggleVisible(object? sender, RoutedEventArgs e) => App.ToggleMainWindow();

    private void OnMenuEditProfile(object? sender, RoutedEventArgs e) => App.OpenProfileEditor();

    private void OnMenuOpenSettings(object? sender, RoutedEventArgs e) => App.OpenSettings();

    private void OnMenuExit(object? sender, RoutedEventArgs e) => App.ExitApp();
}
