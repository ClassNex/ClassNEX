using Avalonia;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Widgets;
using System.Runtime.InteropServices;

namespace ClassNex.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _hoverTimer;
    private readonly List<WidgetBase> _widgets = new();
    private readonly Dictionary<int, StackPanel> _layoutRows = [];
    private readonly Dictionary<int, (double Others, int Schedules)> _rowSizes = [];
    private readonly Dictionary<WidgetConfig, int> _renderedRows = [];
    private TemporaryTimeItem[]? _baseWidgetPeriods;
    private TodaySummary? _widgetPeriodDay;
    private WidgetClassroomPeriod _widgetPeriod;
    private TimeSpan _widgetPeriodStart;
    private bool _hasWidgetFilters, _widgetRulesDirty = true, _hasClassroomContent = true;

    private IntPtr _hwnd;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private double _hoverFadeTarget = 1.0;

    private volatile bool _closed;
    private bool _showingNotification;
    private double _normalWidth;
    private (int X, int Y)? _lastHoverPoint;
    private int _refreshPosted;

#if DEBUG
    private static readonly bool VerifyLayout = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_LAYOUT") == "1";
    private string _hoverDebugRect = "";
    private string _hoverDebugCursor = "";
#endif

    private const double IslandContentMargin = 16;
    private static double EffectiveCornerRadius(double fallback) => AppServices.Settings.OverlayCornerRadius is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 256) : fallback;
    private static double ClassroomCornerRadius => EffectiveCornerRadius(CiPalette.IslandCornerRadius);
    private static double ClassroomReminderRadius => EffectiveCornerRadius(CiPalette.CardCornerRadius);

    public MainWindow()
    {
        InitializeComponent();
        _hoverTransitions = HoverHost.Transitions;
        _railTransitions = OverlayTimeProgressBar.Transitions;
        _maskShadowTransitions = NotificationMaskShadow.Transitions;
        DesktopMotion.Changed += OnMotionPreferenceChanged;
        ApplyMotionPreference();
        ClassroomNotificationColors.Attach(OverlayTimeProgressBar, NotificationMask, NotificationMaskShadow);
        _progressTimer.Tick += SampleOverlayProgress;
        WirePlacement();

        AppServices.SettingsChanged += ApplySettings;
        AppServices.Schedule.ProfileChanged += OnDataChanged;
        AppServices.TimeLayout.Changed += OnDataChanged;
        AppServices.Widgets.Changed += OnWidgetsChanged;
        Screens.Changed += OnDisplayChanged;
        ScalingChanged += OnDisplayChanged;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            RefreshWidgets();
#if DEBUG
            DumpLayoutIfRequested();
#endif
        };

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _hoverTimer.Tick += (_, _) => UpdateHoverFade();

        Deactivated += (_, _) => QueuePlacement();

        CardContent.SizeChanged += (_, args) =>
        {
            if (args.NewSize.Width <= 0) return;
            _normalWidth = args.NewSize.Width + IslandContentMargin * 2;
            if (_returnSizing) SetClassroomReturnSize(DesktopMotion.IsEnabled);
            else if (!_nexPanelActive && !_showingNotification)
            {
                SetIslandWidth(_normalWidth);
                IslandBackground.Height = Math.Max(CiPalette.IslandHeight, args.NewSize.Height);
            }
        };
        NexPanel.SizeChanged += (_, e) => { if (_nexPanelActive) SetIslandWidth(e.NewSize.Width); };

        PropertyChanged += (_, args) =>
        {
            if (args.Property != IsVisibleProperty)
                return;

            UpdateRunningState();
            UpdateReminderAvailability();
            if (!IsVisible) FinishIslandDrag(false);
            if (IsVisible) { RefreshWidgets(); PlayFadeInAnimation(); }
            else if (_showingNotification) ResetNotification();
        };

        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnThemeChanged;
        }
        RebuildWidgets();
        ApplySettings();
        RefreshWidgets();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        EditBarHost.Children.Clear();
        ReloadManagedSettings();
    });

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (!_closed) { ApplySettings(); RefreshWidgets(); }
    });

    private void UpdateRunningState()
    {
        _lastHoverPoint = null;
        if (IsVisible && !_closed) _timer.Start(); else _timer.Stop();
        if (IsVisible && !_closed && !_editMode && !_readingOverflow && AppServices.Settings.HoverOpacity < 0.999) _hoverTimer.Start();
        else { _hoverTimer.Stop(); HoverHost.Opacity = _hoverFadeTarget = 1; }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        MaskTitleViewport.ScrollChanged -= OnReminderScrollChanged;
        NotificationBodyViewport.ScrollChanged -= OnReminderScrollChanged;
        _readingOverflow = false;
        FinishIslandDrag(false);
        UpdateRunningState();
        StopNotificationOverlayMessageTicker();
        StopOverlayProgress();
        _progressTimer.Tick -= SampleOverlayProgress;
        ++_presentation;
        StopClassroomReturn();
        DesktopMotion.Changed -= OnMotionPreferenceChanged;
        AppServices.SettingsChanged -= ApplySettings;
        AppServices.Schedule.ProfileChanged -= OnDataChanged;
        AppServices.TimeLayout.Changed -= OnDataChanged;
        AppServices.Widgets.Changed -= OnWidgetsChanged;
        Screens.Changed -= OnDisplayChanged;
        ScalingChanged -= OnDisplayChanged;
        if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeChanged;
        UpdateReminderAvailability();
        base.OnClosed(e);
    }

    public void ApplySettings()
    {
        if (_closed) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ApplySettings); return; }
        var s = AppServices.Settings;
        if (s.IsClickThrough || _editMode) FinishIslandDrag(false);
        SynchronizeWidgetInstances();

        Topmost = s.Topmost;

        var scale = Math.Clamp(s.EffectiveScale, 0.4, 5.0);
        UpdatePlacementBudget(PlacementScreen());
        if (RootScale.LayoutTransform is ScaleTransform rootScale)
        {
            rootScale.ScaleX = scale;
            rootScale.ScaleY = scale;
        }

        IslandBackground.Background = CiPalette.IslandSurfaceBrush();
        IslandBackground.BoxShadow = CiPalette.IslandShadow();
        IslandSheen.Background = NexSheen.Background = CiPalette.IslandSheenBrush();
        NexPanel.Background = IslandBackground.Background;
        IslandBackground.Opacity = _nexPanelActive ? 0 : Math.Clamp(s.BackgroundOpacity, 0, 1);
        var radius = ClassroomCornerRadius;
        IslandBackground.CornerRadius = NexPanel.CornerRadius = new CornerRadius(radius);
        IslandSheen.CornerRadius = NexSheen.CornerRadius = new CornerRadius(Math.Max(0, radius - 1));
        NotificationMaskBg.Fill = CiPalette.AccentBrush();

        if (!_showingNotification && !_nexPanelActive && _normalWidth > 0) SetIslandWidth(_normalWidth);

        var vertical = s.Orientation == LayoutOrientation.Vertical;
        WidgetHost.Orientation = vertical || _layoutRows.Count > 0 ? Orientation.Vertical : Orientation.Horizontal;
        WidgetHost.Spacing = _layoutRows.Count > 0 ? 4 : 0;
        foreach (var row in _layoutRows.Values) row.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        UpdateWidgetEdges(vertical);
        WidgetViewport.HorizontalScrollBarVisibility = vertical ? ScrollBarVisibility.Disabled : _editMode ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        WidgetViewport.VerticalScrollBarVisibility = !vertical && _layoutRows.Count == 0 ? ScrollBarVisibility.Disabled : _editMode ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
        if (!_showingNotification) IslandBackground.Height = _nexPanelActive ? CiPalette.IslandHeight : Math.Max(CiPalette.IslandHeight, CardContent.DesiredSize.Height);

        EditBarHost.IsVisible = _editMode;
        if (_editMode && EditBarHost.Children.Count == 0)
            BuildEditBar();

        if (_showingNotification)
        {
            ApplyNotificationBackdrop(!_returnSizing);
            if (_returnSizing) SetClassroomReturnSize(DesktopMotion.IsEnabled);
            else if (!_overlayExiting) ArrangeNotification();
        }

        ApplyClickThrough();
        UpdateRunningState();
        if (IsVisible) RefreshWidgets();

        QueuePlacement();
    }

    private void RebuildWidgets()
    {
        WidgetHost.Children.Clear();
        _widgets.Clear();
        _layoutRows.Clear();
        _renderedRows.Clear();

        var visible = WidgetConfig.InDisplayOrder(AppServices.Widgets.Widgets.Where(w => (_editMode || w.IsEnabled) && WidgetRegistry.Find(w.Type) != null)).ToList();
        var multiple = visible.Count > 0 && visible[0].Row != visible[^1].Row;
        var vertical = AppServices.Settings.Orientation == LayoutOrientation.Vertical;
        WidgetHost.Orientation = multiple || vertical ? Orientation.Vertical : Orientation.Horizontal;
        WidgetHost.Spacing = multiple ? 4 : 0;
        StackPanel host = WidgetHost;
        foreach (var config in visible)
        {
            var widget = WidgetFactory.Create(config);
            if (widget is null)
                continue;
            widget.ApplyPresentation();
            if (multiple && !_layoutRows.TryGetValue(config.Row, out host!))
            {
                host = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                _layoutRows.Add(config.Row, host);
                WidgetHost.Children.Add(host);
            }
            _widgets.Add(widget);
            _renderedRows[config] = config.Row;

            if (_editMode)
            {

                var item = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                item.Children.Add(BuildComponentToolbar(config));
                item.Children.Add(widget.DisplayView);
                host.Children.Add(item);
            }
            else
            {
                host.Children.Add(widget.DisplayView);
            }
        }

        if (_editMode)
            host.Children.Add(BuildAddComponentButton(compact: true));

        if (_widgets.Count == 0 && !_editMode)
        {
            WidgetHost.Children.Add(new Border
            {
                Name = "OverlayWidgetsHint", Cursor = new Cursor(StandardCursorType.Hand),
                Background = CiPalette.ChipSurfaceBrush(),
                BorderBrush = CiPalette.ChipBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "未启用组件 · 打开组件布局",
                    Foreground = CiPalette.WithOpacity(CiPalette.PrimaryTextBrush(), 0.66),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        InvalidateWidgetVisibility();
        UpdateWidgetEdges(AppServices.Settings.Orientation == LayoutOrientation.Vertical);
    }

    private void SynchronizeWidgetInstances()
    {
        var i = 0;
        foreach (var config in WidgetConfig.InDisplayOrder(AppServices.Widgets.Widgets.Where(w => (_editMode || w.IsEnabled) && WidgetRegistry.Find(w.Type) != null)))
        {
            if (i >= _widgets.Count || !ReferenceEquals(_widgets[i].Config, config) || _widgets[i].Type != config.Type || _renderedRows.GetValueOrDefault(config) != config.Row)
            {
                RebuildWidgets();
                return;
            }
            _widgets[i++].ApplyPresentation();
        }
        if (i != _widgets.Count) RebuildWidgets();
        else InvalidateWidgetVisibility();
    }

    private void InvalidateWidgetVisibility()
    {
        _widgetRulesDirty = true;
        _hasWidgetFilters = _widgets.Any(w => w.Config.DisplayWhen != WidgetDisplayWhen.Always);
        _baseWidgetPeriods = null;
    }

    private bool UpdateClassroomVisibility(TodaySummary day, TimeSpan time)
    {
        var useFilters = _hasWidgetFilters && !_editMode;
        if (!_widgetRulesDirty && (!useFilters || ReferenceEquals(_widgetPeriodDay, day) && time >= _widgetPeriodStart && time < _widgetPeriod.Until)) return false;
        var previous = _widgetPeriod;
        var force = _widgetRulesDirty;
        _widgetRulesDirty = false;
        if (useFilters)
        {
            _widgetPeriodDay = day;
            _widgetPeriodStart = time;
            _widgetPeriod = WidgetClassroomPeriod.At(day, time, _baseWidgetPeriods ??= TemporaryTimeItem.FromBase(AppServices.TimeLayout.Layout)
                .Where(i => i.Kind is ClassTimeKind.Break or ClassTimeKind.Action && i.End > i.Start).ToArray());
            if (!force && previous.Kind == _widgetPeriod.Kind && previous.NoLessonsToday == _widgetPeriod.NoLessonsToday) return false;
        }
        var changed = false;
        foreach (var widget in _widgets) changed |= widget.SetClassroomVisibility(!useFilters || _widgetPeriod.Allows(widget.Config.DisplayWhen));
        return changed;
    }

    private void UpdateWidgetEdges(bool vertical)
    {
        foreach (var row in _layoutRows.Values) row.IsVisible = _editMode || row.Children.Any(c => c.IsVisible);
        WidgetBase? next = null;
        for (var i = _widgets.Count - 1; i >= 0; i--)
        {
            var widget = _widgets[i];
            if (!widget.View.IsVisible) continue;
            if (next?.Config.Row != widget.Config.Row) next = null;
            if (widget is DateWidget date) date.HasFollowingItem = !vertical && next is not null and not DividerWidget;
            else if (widget is ClockWidget clock) clock.View.Margin = !vertical && next != null ? new Thickness(0, 0, 6, 0) : default;
            next = widget;
        }
        _hasClassroomContent = next != null || _widgets.Count == 0;
        UpdateClassroomSurface();
    }

    private void UpdateClassroomSurface()
    {
        var visible = _hasClassroomContent || _showingNotification || _editMode || _nexPanelActive || SkeletonHost.IsVisible;
        if (visible == LinePanel.IsHitTestVisible) return;
        LinePanel.Opacity = visible ? 1 : 0;
        LinePanel.IsHitTestVisible = visible;
        ApplyClickThrough();
        QueuePlacement();
    }

    private void RefreshWidgets() => RefreshWidgetsAt(AppServices.ClassroomNow);

    internal void RefreshWidgetsAt(DateTime now)
    {
        if (_widgets.Count == 0)
            return;

        var profile = AppServices.Schedule.Profile;
        var today = AppServices.Day.Read(now);
        var visibilityChanged = UpdateClassroomVisibility(today, now.TimeOfDay);
        var vertical = AppServices.Settings.Orientation == LayoutOrientation.Vertical;
        var rowSizes = _layoutRows.Count > 0 ? _rowSizes : null;
        rowSizes?.Clear();
        (double Others, int Schedules) single = default;
        var needsCountdown = false;
        foreach (var widget in _widgets)
        {
            if (!widget.AllowedInClassroom) continue;
            var size = rowSizes?.GetValueOrDefault(widget.Config.Row) ?? single;
            if (widget is ScheduleWidget) size.Schedules++;
            else if (!vertical && widget.View.IsVisible) size.Others += Math.Max(widget.DisplayView.DesiredSize.Width, widget.DisplayView.Bounds.Width);
            if (rowSizes == null) single = size; else rowSizes[widget.Config.Row] = size;
            needsCountdown |= widget is CountdownWidget && widget.Config.CountdownTarget is null ||
                              widget is TextWidget && widget.Config.Text?.Contains("{countdown}", StringComparison.Ordinal) == true;
        }

        var ctx = new WidgetContext
        {
            Settings = AppServices.Settings,
            Profile = profile,
            Today = today,
            Now = now,
            CountdownText = needsCountdown ? AppServices.Time.GetCountdownText(today.Slots, now.TimeOfDay) : "",
            ScheduleWidth = WidthFor(single),
        };
        double WidthFor((double Others, int Schedules) size) => Math.Max(100, (AvailableIslandWidth - 2 * IslandContentMargin - size.Others) / Math.Max(size.Schedules, 1));

        foreach (var widget in _widgets)
        {
            if (!widget.AllowedInClassroom) continue;
            try
            {
                var previouslyVisible = widget.View.IsVisible;
                if (rowSizes != null && widget is ScheduleWidget) ctx.ScheduleWidth = WidthFor(rowSizes[widget.Config.Row]);
                widget.Refresh(ctx);
                visibilityChanged |= previouslyVisible != widget.View.IsVisible;
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("Widget {0}: {1}", widget.Type, error.Message);
            }
        }
        if (visibilityChanged) UpdateWidgetEdges(vertical);
    }

    private void OnDataChanged()
    {
        if (_closed || Interlocked.Exchange(ref _refreshPosted, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _refreshPosted, 0);
            InvalidateWidgetVisibility();
            if (!_closed && IsVisible) RefreshWidgets();
        });
    }

#if DEBUG

    private void DumpLayoutIfRequested()
    {
        if (!VerifyLayout)
            return;

        try
        {
            var schedule = _widgets.OfType<ScheduleWidget>().FirstOrDefault();
            var line =
                $"[{DateTime.Now:HH:mm:ss}] win={Bounds.Width:0.0}x{Bounds.Height:0.0}@({Position.X},{Position.Y}) " +
                $"island={IslandBackground.Bounds.Width:0.0}x{IslandBackground.Bounds.Height:0.0}@{IslandBackground.Bounds.X:0.0}" +
                $"(prop W={IslandBackground.Width:0.0}) " +
                $"content={CardContent.Bounds.Width:0.0}x{CardContent.Bounds.Height:0.0} " +
                $"screen={Screens.Primary?.Bounds.Width ?? 0}x{Screens.Primary?.Bounds.Height ?? 0}" +
                $" scaling={Screens.Primary?.Scaling ?? 0:0.00} " +
                $"scale={(RootScale.LayoutTransform as ScaleTransform)?.ScaleX ?? 0:0.00} " +
                $"hoverRect={_hoverDebugRect} cursor={_hoverDebugCursor} | " +
                $"{(schedule is null ? "无课表组件" : schedule.DebugLayout())}";

            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "_verify_layout.log"), line + "\n");
        }
        catch
        {

        }
    }
#endif

    private void OnWidgetsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (_closed) return;
        SynchronizeWidgetInstances();
        RefreshWidgets();
    });

    public void ReloadManagedSettings()
    {
        RebuildWidgets();
        ApplySettings();
        RefreshWidgets();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        ApplyClickThrough();
        UpdateRunningState();
        QueuePlacement();

        var keepSkeleton = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SKELETON") == "1";
        if (AppServices.Settings.ShowStartupSplash && !keepSkeleton)
        {
            ShowSkeleton();
            DispatcherTimer.RunOnce(HideSkeleton, TimeSpan.FromMilliseconds(380));
        }

        if (double.IsNaN(IslandBackground.Width))
            IslandBackground.Width = 0;
        SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);
#if DEBUG
        App.CheckClockRestart(this);
#endif
    }

    private bool _nexPanelActive;

    public void ShowNexPanel(string text, bool placeholder)
    {
        if (_editMode || string.IsNullOrWhiteSpace(text)) return;
        if (!IsVisible) Show();
        NexPanelText.Text = text;
        NexPanelText.Foreground = CiPalette.PrimaryTextBrush();
        NexPanelText.Opacity = placeholder ? 0.68 : 1;
        if (_nexPanelActive) return;
        _nexPanelActive = true;
        if (_showingNotification) ResetNotification();
        UpdateClassroomSurface();
        UpdateReminderAvailability();
        IslandBackground.Opacity = 0;
        NexPanel.Background = IslandBackground.Background;
        NexPanel.IsVisible = true;
        NexPanel.Opacity = 1;
        CardContent.IsVisible = false;
        FitIslandToActivePanel();
        if (AppServices.Settings.Topmost && _hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void HideNexPanel()
    {
        if (!_nexPanelActive) return;
        _nexPanelActive = false;
        NexPanel.IsVisible = false;
        UpdateClassroomSurface();
        CardContent.IsVisible = true;
        CardContent.Opacity = 1;
        IslandBackground.Opacity = Math.Clamp(AppServices.Settings.BackgroundOpacity, 0, 1);
        FitIslandToActivePanel();
        UpdateReminderAvailability();
    }

    private void FitIslandToActivePanel()
    {
        var panel = _nexPanelActive ? NexPanel : CardContent;
        panel.UpdateLayout();
        var bounds = panel.Bounds.Size;
        var margins = panel.Margin.Left + panel.Margin.Right;
        if (panel.IsArrangeValid && bounds.Width > 0 && bounds.Height > 0)
        {
            SetIslandWidth(bounds.Width + (_nexPanelActive ? 0 : margins));
            IslandBackground.Height = Math.Max(CiPalette.IslandHeight, bounds.Height);
        }
        else
        {
            panel.Measure(new Size(AvailableIslandWidth, double.PositiveInfinity));
            SetIslandWidth(panel.DesiredSize.Width - (_nexPanelActive ? margins : 0));
            IslandBackground.Height = Math.Max(CiPalette.IslandHeight, Math.Ceiling(panel.DesiredSize.Height));
        }
    }

    private void ShowSkeleton()    {
        var brush = CiPalette.SkeletonBrush();
        SkeletonBlock1.Background = brush;
        SkeletonBlock2.Background = brush;
        SkeletonHost.IsVisible = true;
        WidgetHost.IsVisible = false;
        UpdateClassroomSurface();
    }

    private void HideSkeleton()
    {
        if (!SkeletonHost.IsVisible)
            return;

        SkeletonHost.IsVisible = false;
        WidgetHost.IsVisible = true;
        UpdateClassroomSurface();
    }

    private double _availableIslandWidth = 960;
    private double AvailableIslandWidth => _availableIslandWidth;

    private void SetIslandWidth(double target, bool reminder = false)
    {
        if (!double.IsFinite(target) || target <= 0) return;
        var width = Math.Min(AvailableIslandWidth, Math.Ceiling(target));
        IslandBackground.Classes.Set("reminder-resize", reminder && IsVisible && IslandBackground.IsLoaded && double.IsFinite(IslandBackground.Width) && DesktopMotion.IsEnabled);
        IslandBackground.Width = width;
        _lastHoverPoint = null;
        QueuePlacement();
    }
    private void PlayFadeInAnimation()
    {
        var compositionVisual = ElementComposition.GetElementVisual(CardHost);
        if (compositionVisual is null)
            return;

        if (!DesktopMotion.IsEnabled)
        {
            compositionVisual.StopAnimation(nameof(compositionVisual.Opacity));
            compositionVisual.Opacity = 1;
            return;
        }
        var compositor = compositionVisual.Compositor;
        var anim = compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 0f);
        anim.InsertKeyFrame(1f, 1f, DesktopMotion.EnterEase);
        anim.Duration = DesktopMotion.Enter;
        anim.Target = nameof(compositionVisual.Opacity);
        compositionVisual.StartAnimation(nameof(compositionVisual.Opacity), anim);
    }

    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        WindowsOverlay.SetClickThrough(_hwnd, PassesPointerThrough, keepNonActivating: true);
        if (PassesPointerThrough && _inputRegion != null && WindowsOverlay.SetInputRegion(_hwnd, null, 0, null, 0)) _inputRegion = null;

        WindowsOverlay.SetToolWindow(_hwnd, true);
    }

    private void UpdateHoverFade()
    {
        if (!IsVisible)
        {
            _hoverFadeTarget = 1.0;
            HoverHost.Opacity = 1.0;
            return;
        }

        var cursor = WindowsOverlay.GetCursorPosition();
        if (cursor is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        if (_lastHoverPoint == cursor) return;
        _lastHoverPoint = cursor;

        var islandTopLeft = IslandBackground.PointToScreen(new Point(0, 0));
        var islandBottomRight = IslandBackground.PointToScreen(
            new Point(IslandBackground.Bounds.Width, IslandBackground.Bounds.Height));

        var left = islandTopLeft.X;
        var top = islandTopLeft.Y;
        var right = islandBottomRight.X;
        var bottom = islandBottomRight.Y;

#if DEBUG
        if (VerifyLayout)
        {
            _hoverDebugRect = $"{left},{top}..{right},{bottom}";
            _hoverDebugCursor = $"{cursor.Value.X},{cursor.Value.Y}";
        }
#endif

        var hovered = cursor.Value.X >= left && cursor.Value.X <= right
                      && cursor.Value.Y >= top && cursor.Value.Y <= bottom;

        var target = hovered && !_editMode
            ? Math.Clamp(AppServices.Settings.HoverOpacity, 0.0, 1.0)
            : 1.0;

        if (Math.Abs(_hoverFadeTarget - target) > 0.001)
        {
            _hoverFadeTarget = target;
            HoverHost.Opacity = target;
        }
    }

    private void OnMenuToggleVisible(object? sender, RoutedEventArgs e) => App.ToggleMainWindow();

    private void OnMenuEditWidgets(object? sender, RoutedEventArgs e) => App.OpenSettings("widget-layout");

    private void OnMenuEditProfile(object? sender, RoutedEventArgs e) => App.OpenProfileEditor();

    private void OnMenuOpenSettings(object? sender, RoutedEventArgs e) => App.OpenSettings();

    private void OnMenuOpenNex(object? sender, RoutedEventArgs e) => App.OpenNexChat();

    private void OnMenuResetPlacement(object? sender, RoutedEventArgs e) => ResetPlacement();

    private void OnMenuExit(object? sender, RoutedEventArgs e) => App.ExitApp();
}
