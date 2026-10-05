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

    private IntPtr _hwnd;

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
            EnforcePlacement();
            UpdateHoverFade();
        };
        _timer.Start();

        // 某些全屏窗口会抢走置顶，失焦后重新声明
        Deactivated += (_, _) => EnforcePlacement();

        // 主界面固定位置、不可拖动（用户要求「强制置顶不可移动」）
    }

    /// <summary>强制置顶：主界面始终浮在其它窗口之上，不受设置与焦点影响。</summary>
    private void EnsureTopmost()
    {
        if (!IsVisible)
            return;

        if (!Topmost)
            Topmost = true;
    }

    // ---------- 外观 ----------

    public void ApplySettings()
    {
        var s = AppServices.Settings;

        // 强制置顶
        Topmost = true;

        // 外观参数全部取自 CI：黑底 + 50% 不透明度 + 圆角 8
        RootCard.Background = new SolidColorBrush(CiPalette.CardBackground,
            Math.Clamp(s.BackgroundOpacity, 0.05, 1.0));
        RootCard.CornerRadius = new CornerRadius(CiPalette.CardCornerRadius);

        WidgetHost.Orientation = s.Orientation == LayoutOrientation.Vertical
            ? Orientation.Vertical
            : Orientation.Horizontal;
        WidgetHost.Spacing = s.Orientation == LayoutOrientation.Vertical ? 6 : 16;

        // 鼠标穿透设置可能被改动
        ApplyClickThrough();

        // 位置固定为「屏幕顶端 + 水平居中」（对齐 CI），不再使用设置里的坐标
        EnforcePlacement();
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

    // ---------- 位置：与 CI 一致，停靠屏幕顶端 + 水平居中，且不可拖动 ----------

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        ApplyClickThrough();
        EnforcePlacement();
    }

    /// <summary>应用「鼠标穿透」：开启后点击落到后方窗口，不再被主界面挡住。</summary>
    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        WindowsOverlay.SetClickThrough(_hwnd, AppServices.Settings.IsClickThrough);

        // 与 CI 的 WindowFeatures.ToolWindow 等价：主界面不进 Alt+Tab 列表，
        // 这样按 Alt+Tab 切换窗口时浮窗不会被隐藏或关闭。
        WindowsOverlay.SetToolWindow(_hwnd, true);
    }

    /// <summary>
    /// 鼠标移入淡化。因为点击穿透后窗口收不到鼠标消息，这里改为按秒轮询全局光标位置，
    /// 判断光标是否落在主界面矩形内，从而既保持穿透、又能做出悬停淡化。
    /// </summary>
    private void UpdateHoverFade()
    {
        var cursor = WindowsOverlay.GetCursorPosition();
        if (cursor is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var scale = Screens.Primary?.Scaling ?? 1.0;
        if (scale <= 0)
            scale = 1.0;

        var left = Position.X;
        var top = Position.Y;
        var right = left + Bounds.Width * scale;
        var bottom = top + Bounds.Height * scale;

        var hovered = cursor.Value.X >= left && cursor.Value.X <= right
                      && cursor.Value.Y >= top && cursor.Value.Y <= bottom;

        var target = hovered
            ? Math.Clamp(AppServices.Settings.HoverOpacity, 0.05, 1.0)
            : 1.0;

        if (Math.Abs(RootCard.Opacity - target) > 0.01)
            RootCard.Opacity = target;
    }

    /// <summary>
    /// 强制置顶 + 对齐 CI 的停靠。依据 CI 的 data\Settings.json：
    ///   WindowDockingLocation = 1（屏幕顶部）、WindowDockingOffsetX/Y = 0（无偏移）
    ///   窗口按内容自适应宽度后水平居中（CI 卡片 1647 宽 = 内容宽，两侧各 136 为居中留白）
    /// </summary>
    private void EnforcePlacement()
    {
        if (!IsVisible)
            return;

        if (!Topmost)
            Topmost = true;

        var screen = Screens.Primary;
        if (screen is null)
            return;

        var wa = screen.WorkingArea;
        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;

        // 逻辑尺寸 → 物理像素
        var widthPx = (int)Math.Round(Bounds.Width * scale);

        var x = wa.X + Math.Max(0, (wa.Width - widthPx) / 2);
        var y = wa.Y; // WindowDockingOffsetY = 0 → 贴顶

        var target = new PixelPoint(x, y);
        if (Position != target)
            Position = target;
    }

    // ---------- 右键菜单 ----------

    private void OnMenuToggleVisible(object? sender, RoutedEventArgs e) => App.ToggleMainWindow();

    private void OnMenuEditWidgets(object? sender, RoutedEventArgs e) => App.OpenSettings("widgets");

    private void OnMenuEditProfile(object? sender, RoutedEventArgs e) => App.OpenProfileEditor();

    private void OnMenuOpenSettings(object? sender, RoutedEventArgs e) => App.OpenSettings();

    private void OnMenuExit(object? sender, RoutedEventArgs e) => App.ExitApp();
}
