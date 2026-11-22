using ClassNex.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

public sealed partial class NativeOverlayWindow
{
    private enum Surface { Widgets, Nex, Mask, Alert }

    private readonly DesktopBridge _bridge;
    private readonly DispatcherQueue _ui;
    private readonly nint _hwnd;
    private readonly OverlappedPresenter _presenter;
    private readonly DispatcherQueueTimer _clock;
    private readonly DispatcherQueueTimer _hover;
    private readonly DispatcherQueueTimer _turn;
    private readonly Border _island = new() { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
    private readonly Viewbox _view = new() { Stretch = Stretch.Fill };
    private readonly StackPanel _widgets = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _scroller = new()
    {
        HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Enabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        ZoomMode = ZoomMode.Disabled, IsTabStop = false,
        HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly Border _widgetPage = new() { Padding = new Thickness(13, 3, 13, 3) };
    private readonly TextBlock _nexLabel;
    private readonly Border _nexPage;
    private readonly MenuFlyout _menu = new();
    private readonly AccessibilitySettings _contrast = new();
    private OverlayPalette _palette;
    private SolidColorBrush _widgetSurface = null!;
    private bool _paintDirty = true;
    private AppSettings? _settings;
    private Surface _surface;
    private string? _nexText;
    private double _scale = 1, _backgroundOpacity = -1, _hoverOpacity = 1, _shownOpacity = 1;
    private RectInt32 _bounds;
    private int _regionRadius;
    private bool? _passthrough;
    private bool _closed, _layoutDirty = true, _haveBounds, _refreshing;
    private volatile bool _visible;

    public NativeOverlayWindow(DesktopBridge bridge)
    {
        _bridge = bridge;
        _ui = DispatcherQueue;
        _palette = new OverlayPalette(Application.Current.RequestedTheme == ApplicationTheme.Dark, _contrast.HighContrast, _accessibility);
        Title = "ClassNEX 桌面课表";
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _presenter = (OverlappedPresenter)AppWindow.Presenter;
        _presenter.IsResizable = _presenter.IsMaximizable = _presenter.IsMinimizable = false;
        _presenter.SetBorderAndTitleBar(false, false);
        AppWindow.IsShownInSwitchers = false;
        SystemBackdrop = new DesktopAcrylicBackdrop();
        _scroller.Content = _widgets;
        _scroller.SizeChanged += (_, _) => RequestCourseInView();
        _widgetPage.Child = _scroller;
        _nexLabel = Label(16);
        _nexLabel.TextWrapping = TextWrapping.Wrap;
        _nexPage = new Border { Padding = new Thickness(14, 6, 14, 6), Child = _nexLabel, VerticalAlignment = VerticalAlignment.Center };
        _nexPage.Tapped += (_, _) => GestureCommand("nex");
        InitAlerts();
        AutomationProperties.SetAutomationId(_island, "DesktopOverlay");
        AutomationProperties.SetAutomationId(_widgets, "DesktopWidgets");
        AutomationProperties.SetAutomationId(_nexPage, "NexOverlay");
        AutomationProperties.SetAutomationId(_maskPage, "ReminderMask");
        AutomationProperties.SetAutomationId(_coursePage, "ReminderCourse");
        AutomationProperties.SetAutomationId(_messagePage, "ReminderMessage");
        AutomationProperties.SetAutomationId(_track, "ReminderTrack");
        _island.Child = _widgetPage;
        _view.Child = _island;
        WirePlacementGestures();
        AddCommand("应用设置", "settings");
        AddCommand("编辑主界面组件…", "widgets");
        AddCommand("编辑档案", "profile");
        AddCommand("NEX Intelligence", "nex");
        _menu.Items.Add(new MenuFlyoutSeparator());
        var recenter = new MenuFlyoutItem { Text = "恢复顶部居中", Icon = new SymbolIcon(Symbol.BackToWindow) };
        recenter.Click += (_, _) => ResetPlacement();
        _menu.Items.Add(recenter);
        AddCommand("隐藏主界面", "toggle-overlay");
        AddCommand("退出 ClassNEX", "exit");
        _island.ContextFlyout = _menu;
        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(1);
        _clock.Tick += (_, _) => RefreshCore(false);
        _hover = DispatcherQueue.CreateTimer();
        _hover.Interval = TimeSpan.FromMilliseconds(150);
        _hover.Tick += (_, _) => UpdateHover();
        _turn = DispatcherQueue.CreateTimer();
        _turn.Interval = TimeSpan.FromSeconds(10);
        _turn.Tick += (_, _) => TurnAlertPage();
        _view.Loaded += (_, _) => { if (_visible) RefreshCore(true); };
        _view.ActualThemeChanged += (_, _) => { if (_visible) RefreshCore(true); };
        _accessibility.ColorValuesChanged += SystemColorsChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _accessibility.ColorValuesChanged -= SystemColorsChanged;
            _visible = false;
            _clock.Stop();
            _hover.Stop();
            StopAlertAnimations();
        };
        Content = _view;
    }

    public void ShowOverlay(bool activate = false) => OnDispatcher(() =>
    {
        var wasVisible = _visible;
        _visible = true;
        if (!wasVisible && _nexText is not null) { SetText(_nexLabel, _nexText); SelectSurface(Surface.Nex); }
        RefreshCore(true);
        if (!wasVisible || activate) AppWindow.Show(activate);
        if (!_clock.IsRunning) _clock.Start();
    });

    public void HideOverlay() => OnDispatcher(() =>
    {
        if (!_visible) return;
        _visible = false;
        FinishDrag(false);
        AppWindow.Hide();
        _clock.Stop();
        _hover.Stop();
        _ignoreContinuation = _surface is Surface.Alert or Surface.Mask;
        StopAlertAnimations();
        _nexText = null;
        SelectSurface(Surface.Widgets);
        SetHoverOpacity(1);
    });

    public void RefreshOverlay() => OnDispatcher(() => { if (_visible) RefreshCore(true); });

    public void SetNexText(string? text) => OnDispatcher(() =>
    {
        var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (value == _nexText) return;
        _nexText = value;
        if (_visible && _surface is Surface.Widgets or Surface.Nex) RestoreBaseSurface();
    });

    private void OnDispatcher(Action action)
    {
        if (!_ui.HasThreadAccess) _ui.TryEnqueue(() => { if (!_closed) action(); });
        else if (!_closed) action();
    }

    private void AddCommand(string title, string command)
    {
        var item = new MenuFlyoutItem { Text = title };
        item.Click += (_, _) => _bridge.Command(command);
        _menu.Items.Add(item);
    }

    private void SystemColorsChanged(UISettings sender, object args) => InvalidatePalette();

    private void InvalidatePalette() => OnDispatcher(() =>
    {
        _paintDirty = true;
        if (_visible) RefreshCore(true);
    });

    private void PaintSurface()
    {
        _island.BorderBrush = _palette.SurfaceStroke;
        _island.Background = _surface switch { Surface.Mask => _palette.Mask, Surface.Alert => _palette.Selected, _ => _widgetSurface };
    }

    private void RefreshCore(bool settingsChanged)
    {
        if (!_visible || _closed || _refreshing) return;
        _refreshing = true;
        try
        {
            var highContrast = _contrast.HighContrast;
            if (settingsChanged || _settings is null || highContrast != _palette.HighContrast)
                ApplyPreferences(_bridge.ReadSettings(), highContrast);
            if (_surface == Surface.Widgets) UpdateWidgets(_bridge.ReadOverlay());
            MeasureAndPlace();
        }
        finally { _refreshing = false; }
    }

    private void ApplyPreferences(AppSettings settings, bool highContrast)
    {
        _settings = settings;
        var theme = settings.ThemeMode?.ToLowerInvariant() switch
        {
            "dark" => ElementTheme.Dark, "light" => ElementTheme.Light, _ => ElementTheme.Default
        };
        if (_view.RequestedTheme != theme) _view.RequestedTheme = theme;
        var dark = theme == ElementTheme.Dark || theme == ElementTheme.Default && _view.ActualTheme == ElementTheme.Dark;
        var changeTheme = _paintDirty || highContrast != _palette.HighContrast || dark != _palette.Dark;
        if (changeTheme) { _paintDirty = false; _palette = new OverlayPalette(dark, highContrast, _accessibility); }
        var opacity = highContrast ? 1 : Clamp(settings.BackgroundOpacity, 0, 1, 0.5);
        if (changeTheme || _backgroundOpacity != opacity)
        {
            _backgroundOpacity = opacity;
            _widgetSurface = new SolidColorBrush(_palette.Island.Color) { Opacity = opacity };
            PaintSurface();
            _nexLabel.Foreground = _palette.Ink;
            ApplyAlertPalette();
        }
        _scale = Clamp(settings.EffectiveScale, 0.4, 5, 1);
        _hoverOpacity = highContrast ? 1 : Clamp(settings.HoverOpacity, 0, 1, 1);
        if (_presenter.IsAlwaysOnTop != settings.Topmost) _presenter.IsAlwaysOnTop = settings.Topmost;
        if (_passthrough != settings.IsClickThrough)
        {
            if (settings.IsClickThrough) FinishDrag(false);
            OverlayChrome.SetPassthrough(_hwnd, settings.IsClickThrough);
            _passthrough = settings.IsClickThrough;
            _shownOpacity = 1;
        }
        if (settings.IsClickThrough && _hoverOpacity < 1 && _surface == Surface.Widgets)
        {
            if (!_hover.IsRunning) _hover.Start();
        }
        else { _hover.Stop(); SetHoverOpacity(1); }
        var vertical = settings.Orientation == LayoutOrientation.Vertical;
        _widgets.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        EnsureWidgets(settings, changeTheme);
        _layoutDirty = true;
    }

    private void SelectSurface(Surface next)
    {
        if (_surface != next)
        {
            FinishDrag(false);
            _surface = next;
            _island.Child = next switch { Surface.Mask => _maskPage, Surface.Alert => _alertPage, Surface.Nex => _nexPage, _ => _widgetPage };
            PaintSurface();
        }
        if (next != Surface.Widgets) { _hover.Stop(); SetHoverOpacity(1); }
        else if (_visible && _passthrough == true && _hoverOpacity < 1 && !_hover.IsRunning) _hover.Start();
        _layoutDirty = true;
    }

    private void RestoreBaseSurface()
    {
        SelectSurface(_nexText is null ? Surface.Widgets : Surface.Nex);
        if (_nexText is not null) SetText(_nexLabel, _nexText);
        RefreshCore(false);
    }

    private void UpdateHover()
    {
        if (!_visible || _surface != Surface.Widgets || !_haveBounds) return;
        SetHoverOpacity(OverlayChrome.ContainsPointer(_bounds) ? _hoverOpacity : 1);
    }

    private void SetHoverOpacity(double opacity)
    {
        if (Math.Abs(_shownOpacity - opacity) < .001) return;
        _shownOpacity = opacity;
        if (_passthrough == true) OverlayChrome.SetOpacity(_hwnd, opacity);
    }

    private void MeasureAndPlace()
    {
        if (_settings is null || _moving) return;
        var metrics = OverlayChrome.Environment(_hwnd, _settings.MainWindowLeft, _settings.MainWindowTop);
        if (!_layoutDirty && _haveBounds && _lastMetrics == metrics) return;
        _layoutDirty = false;
        _lastMetrics = metrics;
        var factor = _scale * metrics.Dpi / 96d;
        var maxWidth = Math.Max(1, metrics.Width / factor);
        var maxHeight = Math.Max(1, metrics.Height / factor);
        if (_surface == Surface.Widgets) foreach (var widget in _widgetViews) widget.Schedule?.LimitWidth(maxWidth - 28);
        var measuring = _surface switch { Surface.Widgets => (FrameworkElement)_widgets, Surface.Mask => _maskPage, Surface.Alert => _alertPage, _ => _nexPage };
        measuring.Measure(new Size(float.PositiveInfinity, float.PositiveInfinity));
        var desired = measuring.DesiredSize;
        var width = Math.Clamp(2 + (_surface switch
        {
            Surface.Widgets => desired.Width + 26,
            Surface.Mask => Math.Max(260, desired.Width),
            Surface.Alert => Math.Clamp(desired.Width, 416, 760),
            _ => Math.Min(640, desired.Width)
        }), 1, maxWidth);
        if (_surface != Surface.Widgets) { measuring.Measure(new Size((float)Math.Max(1, width - 2), float.PositiveInfinity)); desired = measuring.DesiredSize; }
        var height = Math.Clamp(Math.Max(44, 2 + desired.Height + (_surface == Surface.Widgets ? 6 : 0)), 1, maxHeight);
        if (_island.Width != width) _island.Width = width;
        if (_island.Height != height) _island.Height = height;
        var w = Math.Clamp((int)Math.Ceiling(width * factor), 1, metrics.Width);
        var h = Math.Clamp((int)Math.Ceiling(height * factor), 1, metrics.Height);
        var rect = new RectInt32(metrics.Auto ? metrics.Left + (metrics.Width - w) / 2 : Math.Clamp(metrics.X, metrics.Left, metrics.Left + metrics.Width - w),
            metrics.Auto ? metrics.Top : Math.Clamp(metrics.Y, metrics.Top, metrics.Top + metrics.Height - h), w, h);
        var radius = Math.Max(1, (int)Math.Round(14 * factor));
        var sizeChanged = !_haveBounds || rect.Width != _bounds.Width || rect.Height != _bounds.Height || radius != _regionRadius;
        if (sizeChanged || rect.X != _bounds.X || rect.Y != _bounds.Y) OverlayChrome.Place(_hwnd, rect);
        if (sizeChanged) OverlayChrome.Round(_hwnd, w, h, radius);
        _regionRadius = radius;
        _bounds = rect;
        _haveBounds = true;
        if (sizeChanged && _surface == Surface.Widgets) RequestCourseInView();
    }

    private OverlayChrome.Metrics _lastMetrics;

    private void SetText(TextBlock block, string text)
    {
        if (block.Text == text) return;
        block.Text = text;
        _layoutDirty = true;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
