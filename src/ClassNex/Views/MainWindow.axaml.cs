using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Widgets;

namespace ClassNex.Views;

/// <summary>
/// 主界面：悬浮在桌面上的组件容器（对应 ClassIsland 的「主界面」）。
/// 显示哪些内容由「应用设置 → 主界面组件」决定。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly List<WidgetBase> _widgets = new();

    public MainWindow()
    {
        InitializeComponent();

        ApplySettings();
        RebuildWidgets();
        RefreshWidgets();

        AppServices.SettingsChanged += ApplySettings;
        AppServices.Schedule.ProfileChanged += OnDataChanged;
        AppServices.TimeLayout.Changed += OnDataChanged;
        AppServices.Widgets.Changed += OnWidgetsChanged;

        // 每秒刷新，用于时钟与倒计时
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            RefreshWidgets();
            EnsureTopmost();
        };
        _timer.Start();

        // 某些全屏窗口会抢走置顶，失焦后重新声明
        Deactivated += (_, _) => EnsureTopmost();

        RootCard.PointerPressed += OnCardPointerPressed;
        RootCard.PointerReleased += OnCardPointerReleased;
    }

    /// <summary>确保主界面处于置顶状态（CI 的主界面同样是置顶浮层）。</summary>
    private void EnsureTopmost()
    {
        if (!IsVisible || !AppServices.Settings.Topmost)
            return;

        if (!Topmost)
            Topmost = true;
    }

    // ---------- 外观 ----------

    public void ApplySettings()
    {
        var s = AppServices.Settings;

        Topmost = s.Topmost;

        // 外观参数全部取自 CI：黑底 + 50% 不透明度 + 圆角 8
        RootCard.Background = new SolidColorBrush(CiPalette.CardBackground,
            Math.Clamp(s.BackgroundOpacity, 0.05, 1.0));
        RootCard.CornerRadius = new CornerRadius(CiPalette.CardCornerRadius);
        RootCard.Cursor = new Cursor(StandardCursorType.SizeAll);

        WidgetHost.Orientation = s.Orientation == LayoutOrientation.Vertical
            ? Orientation.Vertical
            : Orientation.Horizontal;
        WidgetHost.Spacing = s.Orientation == LayoutOrientation.Vertical ? 6 : 16;

        Position = new PixelPoint((int)s.MainWindowLeft, (int)s.MainWindowTop);
    }

    // ---------- 组件 ----------

    private void RebuildWidgets()
    {
        WidgetHost.Children.Clear();
        _widgets.Clear();

        foreach (var config in AppServices.Widgets.Widgets.Where(w => w.IsEnabled).OrderBy(w => w.Order))
        {
            var widget = WidgetFactory.Create(config);
            if (widget is null)
                continue;

            _widgets.Add(widget);
            WidgetHost.Children.Add(widget.View);
        }

        if (_widgets.Count == 0)
        {
            WidgetHost.Children.Add(new TextBlock
            {
                Text = "未启用任何组件 —— 请在「应用设置 → 主界面组件」中添加",
                Foreground = Brushes.White,
                FontSize = 14,
            });
        }
    }

    private void RefreshWidgets()
    {
        if (_widgets.Count == 0)
            return;

        var now = DateTime.Now;
        var profile = AppServices.Schedule.Profile;
        var today = AppServices.Time.GetTodaySummary(profile, now);

        var ctx = new WidgetContext
        {
            Settings = AppServices.Settings,
            Profile = profile,
            Today = today,
            Now = now,
            CountdownText = AppServices.Time.GetCountdownText(today.Slots, now.TimeOfDay),
        };

        foreach (var widget in _widgets)
        {
            try
            {
                widget.Refresh(ctx);
            }
            catch
            {
                // 单个组件异常不影响整体
            }
        }
    }

    private void OnDataChanged() => Dispatcher.UIThread.Post(RefreshWidgets);

    private void OnWidgetsChanged() => Dispatcher.UIThread.Post(() =>
    {
        RebuildWidgets();
        RefreshWidgets();
    });

    // ---------- 拖拽 ----------

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

    private void OnMenuEditWidgets(object? sender, RoutedEventArgs e) => App.OpenSettings("widgets");

    private void OnMenuEditProfile(object? sender, RoutedEventArgs e) => App.OpenProfileEditor();

    private void OnMenuOpenSettings(object? sender, RoutedEventArgs e) => App.OpenSettings();

    private void OnMenuExit(object? sender, RoutedEventArgs e) => App.ExitApp();
}
