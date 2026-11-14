using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using ClassNex.Controls;
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

    private bool _startupPlayed;
    private bool _startupScheduled;

    private static readonly Easing IslandWidthEasing = Easing.Parse("0.65, 0, 0.35, 1.0");

#if DEBUG
    private string _hoverDebugRect = "";
    private string _hoverDebugCursor = "";
#endif

    
    private const double IslandContentMargin = 16;

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

        
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            RefreshWidgets();
            EnforcePlacement();
#if DEBUG
            DumpLayoutIfRequested();
#endif
        };
        _timer.Start();

        
        
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _hoverTimer.Tick += (_, _) => UpdateHoverFade();
        _hoverTimer.Start();

        
        Deactivated += (_, _) => EnforcePlacement();

        
        
        
        
#if DEBUG
        CardContent.SizeChanged += (_, args) =>
        {
            if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_LAYOUT") == "1")
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppContext.BaseDirectory, "_verify_layout.log"),
                    $"  [SizeChanged] content {args.PreviousSize.Width:0.0} -> {args.NewSize.Width:0.0}\n");
            SetIslandWidth(args.NewSize.Width + IslandContentMargin * 2);
        };
#else
        CardContent.SizeChanged += (_, args) => SetIslandWidth(args.NewSize.Width + IslandContentMargin * 2);
#endif

        
        PropertyChanged += (_, args) =>
        {
            if (args.Property != IsVisibleProperty)
                return;

            if (IsVisible)
            {
                if (!_startupPlayed)
                    ScheduleStartupEntrance();
                else
                    PlayFadeInAnimation();
            }
            else
            {
                ResetEntranceVisual();
            }
        };

        
        
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ApplySettings();
                RebuildWidgets();
                RefreshWidgets();
            });
        }

        
    }

    
    private void EnsureTopmost()
    {
        if (!IsVisible)
            return;

        if (!Topmost)
            Topmost = true;
    }

    

    public void ApplySettings()
    {
        var s = AppServices.Settings;

        
        Topmost = true;

        
        
        
        
        var scale = Math.Clamp(s.EffectiveScale, 0.4, 5.0);
        if (RootScale.LayoutTransform is ScaleTransform rootScale)
        {
            rootScale.ScaleX = scale;
            rootScale.ScaleY = scale;
        }

        
        
        
        
        IslandBackground.Background = CiPalette.IslandSurfaceBrush();
        IslandBackground.BorderBrush = CiPalette.IslandBorderBrush();
        IslandBackground.BoxShadow = CiPalette.IslandShadow();
        IslandBackground.Opacity = Math.Clamp(s.BackgroundOpacity, 0.05, 1.0);
        IslandBackground.Height = CiPalette.IslandHeight;
        IslandBackground.CornerRadius = new CornerRadius(CiPalette.IslandCornerRadius);
        NexPanel.CornerRadius = new CornerRadius(CiPalette.IslandCornerRadius);
        IslandSheen.Background = CiPalette.IslandSheenBrush();
        NexSheen.Background = CiPalette.IslandSheenBrush();
        NotificationMaskBg.Fill = CiPalette.SakuraAccentGradient();
        NotificationTrackFill.Background = CiPalette.SakuraAccentBrush();

        
        if (CardContent.Bounds.Width > 0)
            SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);

        
        var vertical = s.Orientation == LayoutOrientation.Vertical;
        WidgetHost.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        WidgetHost.Spacing = 0;
        var editRows = _editMode ? 30 : 0;
        if (vertical)
            IslandBackground.Height = CiPalette.IslandHeight * Math.Max(1, _widgets.Count) + editRows;
        else
            IslandBackground.Height = CiPalette.IslandHeight + editRows;

        
        EditBarHost.IsVisible = _editMode;
        if (_editMode)
            BuildEditBar();

        
        
        var primary = Screens.Primary;
        if (primary is not null)
        {
            var screenScaling = primary.Scaling <= 0 ? 1.0 : primary.Scaling;
            Width = primary.WorkingArea.Width / screenScaling;
        }

        
        ApplyClickThrough();

        
        EnforcePlacement();
    }

    

    private void RebuildWidgets()
    {
        WidgetHost.Children.Clear();
        _widgets.Clear();

        
        
        
        
        
        
        WrapPanelResizingAnimationAssist.SetIsResizingAnimationEnabled(WidgetHost, false);

        foreach (var config in AppServices.Widgets.Widgets.Where(w => w.IsEnabled).OrderBy(w => w.Order))
        {
            var widget = WidgetFactory.Create(config);
            if (widget is null)
                continue;

            _widgets.Add(widget);

            if (_editMode)
            {
                
                var item = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Bottom,
                };
                item.Children.Add(BuildComponentToolbar(config));
                item.Children.Add(widget.View);
                WidgetHost.Children.Add(item);
            }
            else
            {
                WidgetHost.Children.Add(widget.View);
            }
        }

        
        if (_editMode)
            WidgetHost.Children.Add(BuildAddComponentButton(compact: true));

        if (_widgets.Count == 0 && !_editMode)
        {
            WidgetHost.Children.Add(new Border
            {
                Background = CiPalette.ChipSurfaceBrush(),
                BorderBrush = CiPalette.ChipBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "未启用任何组件，请在「应用设置 → 主界面组件」中添加",
                    Foreground = CiPalette.WithOpacity(CiPalette.PrimaryTextBrush(), 0.66),
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                },
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
                
            }
        }
    }

    private void OnDataChanged() => Dispatcher.UIThread.Post(RefreshWidgets);

#if DEBUG
    
    
    
    
    private void DumpLayoutIfRequested()
    {
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_LAYOUT") != "1")
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
        RebuildWidgets();
        RefreshWidgets();
    });

    

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        ApplyClickThrough();
        EnforcePlacement();

        
        
        
        var keepSkeleton = Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SKELETON") == "1";
        if (AppServices.Settings.ShowStartupSplash && !keepSkeleton)
        {
            ShowSkeleton();
            DispatcherTimer.RunOnce(HideSkeleton, TimeSpan.FromMilliseconds(380));
        }
        else if (!keepSkeleton)
        {
            ScheduleStartupEntrance();
        }

        if (double.IsNaN(IslandBackground.Width))
            IslandBackground.Width = 0;
        SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);
    }

    

    private bool _nexPanelActive;

    private TranslateTransform? CardShift => CardContent.RenderTransform as TranslateTransform;

    private TranslateTransform? NexShift => NexPanel.RenderTransform as TranslateTransform;

    
    
    
    
    public void ShowNexPanel(string text, bool placeholder)
    {
        if (!IsVisible)
            Show();

        if (!Topmost)
            Topmost = true;

        NexPanelText.Text = text;
        
        
        NexPanelText.Foreground = CiPalette.PrimaryTextBrush();
        NexPanelText.Opacity = placeholder ? 0.62 : 1.0;

        if (!_nexPanelActive)
        {
            _nexPanelActive = true;

            
            
            NexPanel.Background = IslandBackground.Background;
            NexPanel.BorderBrush = IslandBackground.BorderBrush;
            IslandBackground.Opacity = 0;
            NexPanel.IsVisible = true;

            if (CardShift is { } card)
                card.Y = 0;
            if (NexShift is { } nex)
                nex.Y = 24;
            NexPanel.Opacity = 0;

            Dispatcher.UIThread.Post(() =>
            {
                
                if (CardShift is { } c)
                    c.Y = -26;
                CardContent.Opacity = 0;

                
                if (NexShift is { } n)
                    n.Y = 0;
                NexPanel.Opacity = 1;

                SetIslandWidth(NexPanel.Bounds.Width + IslandContentMargin * 2);
            }, DispatcherPriority.Background);
        }
        else
        {
            SetIslandWidth(NexPanel.Bounds.Width + IslandContentMargin * 2);
        }

        // Raise after the panel is configured so a z-order call can never hide it.
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
            SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);

        Activate();
    }

    
    public void HideNexPanel()
    {
        if (!_nexPanelActive)
            return;

        _nexPanelActive = false;

        if (NexShift is { } n)
            n.Y = 24;
        NexPanel.Opacity = 0;

        if (CardShift is { } c)
            c.Y = 0;
        CardContent.Opacity = 1;

        DispatcherTimer.RunOnce(() =>
        {
            if (_nexPanelActive)
                return;

            NexPanel.IsVisible = false;

            
            IslandBackground.Opacity = Math.Clamp(AppServices.Settings.BackgroundOpacity, 0.05, 1.0);
            SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);
        }, TimeSpan.FromMilliseconds(280));
    }

    
    
    
    
    private void ShowSkeleton()    {
        var brush = CiPalette.SkeletonBrush();
        SkeletonBlock1.Background = brush;
        SkeletonBlock2.Background = brush;
        SkeletonHost.IsVisible = true;
        WidgetHost.IsVisible = false;
    }

    
    private void HideSkeleton()
    {
        if (!SkeletonHost.IsVisible)
            return;

        SkeletonHost.IsVisible = false;
        WidgetHost.IsVisible = true;
        ScheduleStartupEntrance();
    }

    
    private double? _preNotificationIslandWidth;
    private int _notificationTransitionGeneration;
    private ScaleTransform _notificationMaskTurnScale = new();
    private SkewTransform _notificationMaskTurnSkew = new();
    private TranslateTransform _notificationMaskTurnShift = new();
    private ScaleTransform _notificationOverlayTurnScale = new();
    private SkewTransform _notificationOverlayTurnSkew = new();
    private TranslateTransform _notificationOverlayTurnShift = new();
    private DispatcherTimer? _notificationOverlayMessageTimer;
    private bool _notificationMessagePageShown;
    private bool _notificationMessagePageSwitching;

    
    
    
    
    private void ExpandIslandForNotification()
    {
        _preNotificationIslandWidth ??= IslandBackground.Width;

        var scale = Math.Max(0.1, AppServices.Settings.MainWindowScale);
        var full = Bounds.Width / scale;   
        if (full > IslandBackground.Width)
            IslandBackground.Width = full;

        
        if (ElementComposition.GetElementVisual(IslandBackground) is { } visual)
            visual.Scale = new Vector3D(1, 1, 1);
    }

    
    
    
    
    private void StartNotificationTrack()
    {
        
        _trackFullWidth = Math.Max(1, IslandBackground.Width - IslandContentMargin * 2);
        NotificationTrack.Width = _trackFullWidth;
        NotificationTrackFill.Width = _trackFullWidth;
        NotificationTrack.IsVisible = true;

        var visual = ElementComposition.GetElementVisual(NotificationTrackFill);
        if (visual is null)
            return;

        visual.StopAnimation(nameof(visual.Scale));
        visual.CenterPoint = new Vector3D(0, visual.Size.Y / 2, 0);
        visual.Scale = new Vector3D(1, 1, 1);
        var anim = visual.Compositor.CreateVector3DKeyFrameAnimation();
        anim.InsertKeyFrame(0f, new Vector3D(1, 1, 1));
        anim.InsertKeyFrame(1f, new Vector3D(0, 1, 1), new LinearEasing());
        anim.Duration = TimeSpan.FromMilliseconds(6800);
        visual.StartAnimation(nameof(visual.Scale), anim);
    }

    private double _trackFullWidth;

    
    private void RestoreIslandAfterNotification()
    {
        NotificationTrack.IsVisible = false;
        if (_preNotificationIslandWidth is { } previous)
        {
            IslandBackground.Width = previous;
            _preNotificationIslandWidth = null;
        }
    }

    
    public PixelRect? GetIslandScreenRect()
    {
        if (IslandBackground is not { } island || !island.IsLoaded)
            return null;

        var topLeft = island.PointToScreen(new Point(0, 0));
        var bottomRight = island.PointToScreen(new Point(island.Bounds.Width, island.Bounds.Height));
        var size = new PixelSize(
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
        return new PixelRect(topLeft, size);
    }

    
    public PixelPoint? GetIslandCenterOnScreen()
    {
        if (IslandBackground is not { } island || !island.IsLoaded)
            return null;

        return island.PointToScreen(new Point(island.Bounds.Width / 2, island.Bounds.Height / 2));
    }

    
    
    
    
    
    public void ShowNotificationMask(
        string text,
        string leftGlyph = "\uE0FF",
        string rightGlyph = "\uE54F")
    {
        _notificationTransitionGeneration++;
        StopNotificationOverlayMessageTicker();
        ResetNotificationOverlayPages();
        NotificationMaskText.Text = text;
        NotificationMaskLeftIcon.Text = leftGlyph;
        NotificationMaskRightIcon.Text = rightGlyph;
        ResetNotificationMaskTurn();
        ResetNotificationOverlayTurn();
        NotificationMask.Opacity = 1;
        NotificationOverlay.Opacity = 0;
        NotificationMaskContent.Opacity = 0;
        NotificationMaskContent.IsVisible = true;
        NotificationOverlay.IsVisible = false;
        NotificationTrack.IsVisible = false;
        if (ElementComposition.GetElementVisual(NotificationTrackFill) is { } trackVisual)
            trackVisual.StopAnimation(nameof(trackVisual.Scale));
        NotificationMask.IsVisible = true;

        ExpandIslandForNotification();

        CardContent.Opacity = 0;

        NotificationMaskBg.IsOpened = true;

        Avalonia.Controls.Documents.TextElement.SetForeground(NotificationMask, Brushes.Black);
        NotificationMaskText.Foreground = Brushes.Black;
        NotificationMaskLeftIcon.Foreground = Brushes.Black;
        NotificationMaskRightIcon.Foreground = Brushes.Black;

        var easing = Easing.Parse("0.25, 1, 0.5, 1");

        var fade = new Animation
        {
            Delay = TimeSpan.FromMilliseconds(260),
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = easing,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
            },
        };
        _ = fade.RunAsync(NotificationMaskContent);

        var scale = new Animation
        {
            Delay = TimeSpan.FromMilliseconds(260),
            Duration = TimeSpan.FromMilliseconds(750),
            FillMode = FillMode.Forward,
            Easing = easing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.1),
                        new Setter(ScaleTransform.ScaleYProperty, 1.1),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(ScaleTransform.ScaleXProperty, 1.0),
                        new Setter(ScaleTransform.ScaleYProperty, 1.0),
                    },
                },
            },
        };
        _ = scale.RunAsync(NotificationMaskContent);
    }

    
    public void HideNotificationMask()
    {
        var generation = ++_notificationTransitionGeneration;
        StopNotificationOverlayMessageTicker();
        NotificationMaskBg.IsOpened = false;

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0.0) } },
            },
        };
        if (NotificationMask.IsVisible)
            _ = fade.RunAsync(NotificationMaskContent);
        if (NotificationOverlay.IsVisible)
            _ = fade.RunAsync(NotificationOverlay);

        DispatcherTimer.RunOnce(() =>
        {
            if (generation != _notificationTransitionGeneration)
                return;

            NotificationMask.IsVisible = false;
            NotificationOverlay.IsVisible = false;
            NotificationMask.Opacity = 1;
            NotificationOverlay.Opacity = 1;
            ResetNotificationMaskTurn();
            ResetNotificationOverlayTurn();

            CardContent.Opacity = 1;

            RestoreIslandAfterNotification();
        }, TimeSpan.FromMilliseconds(240));
    }

    
    
    
    
    public void ShowNotificationOverlay(
        string left,
        string subject,
        string teacher,
        string time,
        string prefix = "下节课是：",
        string message = "",
        bool verticalPageTurn = false)
    {
        var generation = ++_notificationTransitionGeneration;
        StopNotificationOverlayMessageTicker();
        ResetNotificationOverlayPages();
        NotificationOverlayLeft.Text = left;
        NotificationOverlaySubject.Text = subject;
        NotificationOverlayTeacher.Text = teacher;
        NotificationOverlayTeacher.IsVisible = !string.IsNullOrWhiteSpace(teacher);
        NotificationOverlayTime.Text = time;
        NotificationOverlayPrefix.Text = prefix;
        NotificationOverlayPrefix.IsVisible = !string.IsNullOrWhiteSpace(prefix);
        NotificationOverlayMessagePage.Text = message;
        NotificationOverlay.IsVisible = true;
        StartNotificationTrack();

        if (!string.IsNullOrWhiteSpace(message))
        {
            _notificationOverlayMessageTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10),
            };
            _notificationOverlayMessageTimer.Tick += OnNotificationOverlayMessageTick;
            _notificationOverlayMessageTimer.Start();
        }

        if (NotificationMask.IsVisible)
        {
            PrepareNotificationPageTurn(verticalPageTurn);
            _ = PlayNotificationPageTurnAsync(generation, verticalPageTurn);
            return;
        }

        ResetNotificationOverlayTurn();
        NotificationOverlay.Opacity = 0;
        _ = PlayNotificationOverlayFadeInAsync();
    }

    public void UpdateNotificationOverlayCountdown(string text)
    {
        if (NotificationOverlay.IsVisible)
            NotificationOverlayLeft.Text = text;
    }

    private void OnNotificationOverlayMessageTick(object? sender, EventArgs e)
    {
        _ = ToggleNotificationOverlayMessagePageAsync();
    }

    private async Task ToggleNotificationOverlayMessagePageAsync()
    {
        if (_notificationMessagePageSwitching ||
            !NotificationOverlay.IsVisible ||
            string.IsNullOrWhiteSpace(NotificationOverlayMessagePage.Text))
        {
            return;
        }

        var generation = _notificationTransitionGeneration;
        _notificationMessagePageSwitching = true;

        Control outgoing = _notificationMessagePageShown
            ? NotificationOverlayMessagePage
            : NotificationOverlayCoursePage;
        Control incoming = _notificationMessagePageShown
            ? NotificationOverlayCoursePage
            : NotificationOverlayMessagePage;

        outgoing.RenderTransform = new TranslateTransform();
        incoming.RenderTransform = new TranslateTransform(0, -40);
        incoming.Opacity = 0;
        incoming.IsVisible = true;
        outgoing.ZIndex = 2;
        incoming.ZIndex = 1;

        var easing = Easing.Parse("0.65, 0, 0.35, 1");
        var fadeOut = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = easing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1.0),
                        new Setter(TranslateTransform.YProperty, 0.0),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0),
                        new Setter(TranslateTransform.YProperty, 40.0),
                    },
                },
            },
        };
        var fadeIn = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = easing,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0),
                        new Setter(TranslateTransform.YProperty, -40.0),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1.0),
                        new Setter(TranslateTransform.YProperty, 0.0),
                    },
                },
            },
        };

        await Task.WhenAll(
            fadeOut.RunAsync(outgoing),
            fadeIn.RunAsync(incoming));

        if (generation != _notificationTransitionGeneration || !NotificationOverlay.IsVisible)
        {
            _notificationMessagePageSwitching = false;
            return;
        }

        outgoing.IsVisible = false;
        outgoing.Opacity = 0;
        outgoing.RenderTransform = new TranslateTransform();
        incoming.Opacity = 1;
        incoming.RenderTransform = new TranslateTransform();
        outgoing.ZIndex = 0;
        incoming.ZIndex = 0;
        _notificationMessagePageShown = !_notificationMessagePageShown;
        _notificationMessagePageSwitching = false;
    }

    private void ResetNotificationOverlayPages()
    {
        NotificationOverlayCoursePage.RenderTransform = new TranslateTransform();
        NotificationOverlayCoursePage.Opacity = 1;
        NotificationOverlayCoursePage.IsVisible = true;
        NotificationOverlayCoursePage.ZIndex = 0;
        NotificationOverlayMessagePage.RenderTransform = new TranslateTransform();
        NotificationOverlayMessagePage.Opacity = 0;
        NotificationOverlayMessagePage.IsVisible = false;
        NotificationOverlayMessagePage.ZIndex = 0;
        _notificationMessagePageShown = false;
        _notificationMessagePageSwitching = false;
    }

    private void StopNotificationOverlayMessageTicker()
    {
        if (_notificationOverlayMessageTimer is not { } timer)
            return;

        timer.Stop();
        timer.Tick -= OnNotificationOverlayMessageTick;
        _notificationOverlayMessageTimer = null;
    }

    private void PrepareNotificationPageTurn(bool vertical)
    {
        _notificationMaskTurnScale = new ScaleTransform(1, 1);
        _notificationMaskTurnSkew = new SkewTransform(0, 0);
        _notificationMaskTurnShift = new TranslateTransform();
        NotificationMask.RenderTransform = new TransformGroup
        {
            Children = { _notificationMaskTurnScale, _notificationMaskTurnSkew, _notificationMaskTurnShift },
        };
        NotificationMask.RenderTransformOrigin =
            vertical
                ? new RelativePoint(0.5, -0.10, RelativeUnit.Relative)
                : new RelativePoint(-0.10, 0.5, RelativeUnit.Relative);

        _notificationOverlayTurnScale = vertical
            ? new ScaleTransform(1, 0.06)
            : new ScaleTransform(0.06, 1);
        _notificationOverlayTurnSkew = vertical
            ? new SkewTransform(0, 5)
            : new SkewTransform(5, 0);
        _notificationOverlayTurnShift = vertical
            ? new TranslateTransform(0, 42)
            : new TranslateTransform(42, 0);
        NotificationOverlay.RenderTransform = new TransformGroup
        {
            Children = { _notificationOverlayTurnScale, _notificationOverlayTurnSkew, _notificationOverlayTurnShift },
        };
        NotificationOverlay.RenderTransformOrigin =
            vertical
                ? new RelativePoint(0.5, -0.10, RelativeUnit.Relative)
                : new RelativePoint(-0.10, 0.5, RelativeUnit.Relative);

        NotificationMask.Opacity = 1;
        NotificationOverlay.Opacity = 0;
        NotificationMask.ZIndex = 2;
        NotificationOverlay.ZIndex = 3;
    }

    private async Task PlayNotificationPageTurnAsync(int generation, bool vertical)
    {
        var outgoing = CreateNotificationPageTurnAnimation(true, vertical);
        var incoming = CreateNotificationPageTurnAnimation(false, vertical);
        await Task.WhenAll(
            outgoing.RunAsync(NotificationMask),
            incoming.RunAsync(NotificationOverlay));

        if (generation != _notificationTransitionGeneration)
            return;

        NotificationMaskBg.IsOpened = false;
        NotificationMaskContent.Opacity = 0;
        NotificationMaskContent.IsVisible = false;
        NotificationMask.IsVisible = false;
        NotificationMask.Opacity = 1;
        NotificationOverlay.Opacity = 1;
        ResetNotificationMaskTurn();
        ResetNotificationOverlayTurn();
    }

    private static Animation CreateNotificationPageTurnAnimation(bool outgoing, bool vertical)
    {
        var scaleProperty = vertical
            ? ScaleTransform.ScaleYProperty
            : ScaleTransform.ScaleXProperty;
        var skewProperty = vertical
            ? SkewTransform.AngleYProperty
            : SkewTransform.AngleXProperty;
        var shiftProperty = vertical
            ? TranslateTransform.YProperty
            : TranslateTransform.XProperty;

        if (outgoing)
        {
            return new Animation
            {
                Duration = TimeSpan.FromMilliseconds(360),
                FillMode = FillMode.Forward,
                Easing = Easing.Parse("0.1, 0.9, 0.2, 1"),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(OpacityProperty, 1.0),
                            new Setter(scaleProperty, 1.0),
                            new Setter(skewProperty, 0.0),
                            new Setter(shiftProperty, 0.0),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.38),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(scaleProperty, 0.06),
                            new Setter(skewProperty, -5.0),
                            new Setter(shiftProperty, -18.0),
                        },
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(scaleProperty, 0.06),
                            new Setter(skewProperty, -5.0),
                            new Setter(shiftProperty, -18.0),
                        },
                    },
                },
            };
        }

        return new Animation
        {
            Duration = TimeSpan.FromMilliseconds(360),
            FillMode = FillMode.Forward,
            Easing = Easing.Parse("0.1, 0.9, 0.2, 1"),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0),
                        new Setter(scaleProperty, 0.06),
                        new Setter(skewProperty, 5.0),
                        new Setter(shiftProperty, 42.0),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(0.38),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0),
                        new Setter(scaleProperty, 0.06),
                        new Setter(skewProperty, 5.0),
                        new Setter(shiftProperty, 42.0),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1.0),
                        new Setter(scaleProperty, 1.0),
                        new Setter(skewProperty, 0.0),
                        new Setter(shiftProperty, 0.0),
                    },
                },
            },
        };
    }

    private async Task PlayNotificationOverlayFadeInAsync()
    {
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            FillMode = FillMode.Forward,
            Easing = Easing.Parse("0.25, 1, 0.5, 1"),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
            },
        };
        await fade.RunAsync(NotificationOverlay);
    }

    private void ResetNotificationMaskTurn()
    {
        _notificationMaskTurnScale = new ScaleTransform(1, 1);
        _notificationMaskTurnSkew = new SkewTransform(0, 0);
        _notificationMaskTurnShift = new TranslateTransform();
        NotificationMask.RenderTransform = new TransformGroup
        {
            Children = { _notificationMaskTurnScale, _notificationMaskTurnSkew, _notificationMaskTurnShift },
        };
        NotificationMask.RenderTransformOrigin = RelativePoint.Center;
        NotificationMask.ZIndex = 0;
    }

    private void ResetNotificationOverlayTurn()
    {
        _notificationOverlayTurnScale = new ScaleTransform(1, 1);
        _notificationOverlayTurnSkew = new SkewTransform(0, 0);
        _notificationOverlayTurnShift = new TranslateTransform();
        NotificationOverlay.RenderTransform = new TransformGroup
        {
            Children = { _notificationOverlayTurnScale, _notificationOverlayTurnSkew, _notificationOverlayTurnShift },
        };
        NotificationOverlay.RenderTransformOrigin = RelativePoint.Center;
        NotificationOverlay.ZIndex = 0;
    }

    
    
    
    
    public void WarmNotificationMask()
    {
        NotificationMaskBg.IsOpened = true;
        DispatcherTimer.RunOnce(() => NotificationMaskBg.IsOpened = false, TimeSpan.FromMilliseconds(60));
    }

    
    
    
    
    
    private void SetIslandWidth(double target)
    {
        var previous = IslandBackground.Width;
        IslandBackground.Width = target;

        var visual = ElementComposition.GetElementVisual(IslandBackground);
        if (visual is null)
            return;

        
        visual.CenterPoint = new System.Numerics.Vector3((float)target / 2, (float)(CiPalette.IslandHeight / 2), 0f);

        
        var from = double.IsNaN(previous) || previous <= 0.5
            ? 0f
            : (float)(previous / target);
        if (Math.Abs(from - 1f) < 0.001f)
            return;

        var compositor = visual.Compositor;
        var anim = compositor.CreateVector3KeyFrameAnimation();
        anim.InsertKeyFrame(0f, new System.Numerics.Vector3(from, 1f, 1f));
        anim.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f), IslandWidthEasing);
        anim.Duration = TimeSpan.FromMilliseconds(300);
        anim.Target = nameof(visual.Scale);
        visual.StartAnimation(nameof(visual.Scale), anim);
    }

    
    
    
    
    private void ResetEntranceVisual()
    {
        if (!_startupPlayed)
            return;

        var visual = ElementComposition.GetElementVisual(LinePanel);
        if (visual is not null)
        {
            visual.Opacity = 1f;
            visual.Scale = new Vector3D(1f, 1f, 1f);
        }

        foreach (var child in WidgetHost.Children.OfType<Control>())
        {
            var childVisual = ElementComposition.GetElementVisual(child);
            if (childVisual is null)
                continue;

            childVisual.Opacity = 1f;
        }
    }

    private void ScheduleStartupEntrance()
    {
        if (_startupPlayed || _startupScheduled)
            return;

        _startupScheduled = true;
        DispatcherTimer.RunOnce(() =>
        {
            _startupScheduled = false;
            PlayStartupEntrance();
        }, TimeSpan.FromMilliseconds(80));
    }

    private void PlayStartupEntrance()
    {
        if (_startupPlayed || !IsVisible)
            return;

        _startupPlayed = true;

        var visual = ElementComposition.GetElementVisual(LinePanel);
        if (visual is null)
        {
            PlayFadeInAnimation();
            return;
        }

        var bounds = LinePanel.Bounds;
        visual.CenterPoint = new Vector3D(
            (float)(bounds.Width / 2),
            (float)(bounds.Height / 2),
            0);
        visual.Opacity = 0f;
        visual.Scale = new Vector3D(0.94f, 0.90f, 1f);

        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, Easing.Parse("0.16, 1, 0.3, 1"));
        fade.Duration = TimeSpan.FromMilliseconds(420);
        visual.StartAnimation(nameof(visual.Opacity), fade);

        var scale = compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(0f, new System.Numerics.Vector3(0.94f, 0.90f, 1f));
        scale.InsertKeyFrame(0.62f, new System.Numerics.Vector3(1.012f, 1.006f, 1f), Easing.Parse("0.22, 1, 0.36, 1"));
        scale.InsertKeyFrame(1f, new System.Numerics.Vector3(1f, 1f, 1f));
        scale.Duration = TimeSpan.FromMilliseconds(760);
        visual.StartAnimation(nameof(visual.Scale), scale);

        AnimateWidgetStagger();
    }

    private void AnimateWidgetStagger()
    {
        var children = WidgetHost.Children.OfType<Control>().ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var visual = ElementComposition.GetElementVisual(child);
            if (visual is null)
                continue;

            var delay = TimeSpan.FromMilliseconds(90 + Math.Min(i, 8) * 45);

            visual.Opacity = 0f;

            if (delay <= TimeSpan.Zero)
            {
                StartWidgetEntrance(visual);
                continue;
            }

            DispatcherTimer.RunOnce(() =>
            {
                if (child.IsLoaded)
                    StartWidgetEntrance(visual);
            }, delay);
        }
    }

    private static void StartWidgetEntrance(CompositionVisual visual)
    {
        var compositor = visual.Compositor;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, Easing.Parse("0.16, 1, 0.3, 1"));
        fade.Duration = TimeSpan.FromMilliseconds(430);
        visual.StartAnimation(nameof(visual.Opacity), fade);
    }

    private void PlayFadeInAnimation()
    {
        var compositionVisual = ElementComposition.GetElementVisual(CardHost);
        if (compositionVisual is null)
            return;

        var compositor = compositionVisual.Compositor;
        var anim = compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 0f);
        anim.InsertKeyFrame(1f, 1f, Easing.Parse("0.25, 1, 0.5, 1"));
        anim.Duration = TimeSpan.FromMilliseconds(250);
        anim.Target = nameof(compositionVisual.Opacity);
        compositionVisual.StartAnimation(nameof(compositionVisual.Opacity), anim);
    }

    
    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        
        WindowsOverlay.SetClickThrough(_hwnd, AppServices.Settings.IsClickThrough && !_editMode);

        
        
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

        
        
        var islandTopLeft = IslandBackground.PointToScreen(new Point(0, 0));
        var islandBottomRight = IslandBackground.PointToScreen(
            new Point(IslandBackground.Bounds.Width, IslandBackground.Bounds.Height));

        var left = islandTopLeft.X;
        var top = islandTopLeft.Y;
        var right = islandBottomRight.X;
        var bottom = islandBottomRight.Y;

#if DEBUG
        _hoverDebugRect = $"{left},{top}..{right},{bottom}";
        _hoverDebugCursor = $"{cursor.Value.X},{cursor.Value.Y}";
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

        
        var widthPx = (int)Math.Round(Bounds.Width * scale);

        
        var x = wa.X + (wa.Width - widthPx) / 2;
        var y = wa.Y; 

        var target = new PixelPoint(x, y);
        if (Position != target)
            Position = target;
    }

    

    private void OnMenuToggleVisible(object? sender, RoutedEventArgs e) => App.ToggleMainWindow();

    private void OnMenuEditWidgets(object? sender, RoutedEventArgs e) => App.OpenSettings("widgets");

    private void OnMenuEditProfile(object? sender, RoutedEventArgs e) => App.OpenProfileEditor();

    private void OnMenuOpenSettings(object? sender, RoutedEventArgs e) => App.OpenSettings();

    private void OnMenuExit(object? sender, RoutedEventArgs e) => App.ExitApp();
}
