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

namespace ClassNex.Views;





public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _hoverTimer;
    private readonly List<WidgetBase> _widgets = new();

    private IntPtr _hwnd;

    
    private double _hoverFadeTarget = 1.0;

    private static readonly Easing IslandWidthEasing = Easing.Parse("0.65, 0, 0.35, 1.0");

#if DEBUG
    private string _hoverDebugRect = "";
    private string _hoverDebugCursor = "";
#endif

    
    private const double IslandContentMargin = 12;

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
            if (args.Property == IsVisibleProperty && IsVisible)
                PlayFadeInAnimation();
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

        
        
        
        
        IslandBackground.Background = CiPalette.SurfaceBrush("SolidBackgroundFillColorSecondaryBrush", s.BackgroundOpacity);
        IslandBackground.BorderBrush = CiPalette.IslandBorderBrush();
        IslandBackground.Opacity = Math.Clamp(s.BackgroundOpacity, 0.05, 1.0);
        IslandBackground.Height = 40;

        
        if (CardContent.Bounds.Width > 0)
            SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);

        
        var vertical = s.Orientation == LayoutOrientation.Vertical;
        WidgetHost.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        WidgetHost.Spacing = 0;
        if (vertical)
            IslandBackground.Height = 40 * Math.Max(1, _widgets.Count) + (_editMode ? 30 : 0);
        else
            IslandBackground.Height = 40 + (_editMode ? 30 : 0);

        
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

        
        
        
        if (AppServices.Settings.ShowStartupSplash)
            ShowSkeleton();
        if (!AppServices.Settings.ShowStartupSplash
            || Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SKELETON") == "1")
        {
            
        }
        else
        {
            DispatcherTimer.RunOnce(HideSkeleton, TimeSpan.FromMilliseconds(380));
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
        NexPanelText.Text = text;
        
        
        NexPanelText.Foreground = Brushes.White;
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
        PlayFadeInAnimation();
    }

    
    private double? _preNotificationIslandWidth;

    
    
    
    
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
        
        _trackFullWidth = Math.Max(1, IslandBackground.Width - 24); 
        NotificationTrack.Width = _trackFullWidth;
        NotificationTrackFill.Width = _trackFullWidth;
        NotificationTrack.IsVisible = true;

        if (ElementComposition.GetElementVisual(NotificationTrackFill) is { } fillVisual)
        {
            fillVisual.CenterPoint = new Vector3D(0, fillVisual.Size.Y / 2, 0); 
            fillVisual.Scale = new Vector3D(1, 1, 1);
        }

        
        DispatcherTimer.RunOnce(() =>
        {
            var visual = ElementComposition.GetElementVisual(NotificationTrackFill);
            if (visual is null)
                return;

            visual.CenterPoint = new Vector3D(0, visual.Size.Y / 2, 0);
            var anim = visual.Compositor.CreateVector3DKeyFrameAnimation();
            anim.InsertKeyFrame(0f, new Vector3D(1, 1, 1));
            anim.InsertKeyFrame(1f, new Vector3D(0, 1, 1), new LinearEasing());
            anim.Duration = TimeSpan.FromMilliseconds(1500);
            visual.StartAnimation(nameof(visual.Scale), anim);
        }, TimeSpan.FromMilliseconds(500));
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

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var size = new PixelSize(
            Math.Max(1, (int)Math.Round(island.Bounds.Width * scaling)),
            Math.Max(1, (int)Math.Round(island.Bounds.Height * scaling)));
        return new PixelRect(topLeft, size);
    }

    
    public PixelPoint? GetIslandCenterOnScreen()
    {
        if (IslandBackground is not { } island || !island.IsLoaded)
            return null;

        return island.PointToScreen(new Point(island.Bounds.Width / 2, island.Bounds.Height / 2));
    }

    
    
    
    
    
    public void ShowNotificationMask(string text)
    {
        NotificationMaskText.Text = text;
        NotificationMask.IsVisible = true;

        
        ExpandIslandForNotification();

        
        CardContent.Opacity = 0;

        
        NotificationMaskBg.IsOpened = true;

        
        
        Avalonia.Controls.Documents.TextElement.SetForeground(NotificationMask, Brushes.Black);
        NotificationMaskText.Foreground = Brushes.Black;

        
        
        
        
        
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
        _ = fade.RunAsync(NotificationMaskContent);

        
        DispatcherTimer.RunOnce(() =>
        {
            NotificationMask.IsVisible = false;
            NotificationOverlay.IsVisible = false;
            
            CardContent.Opacity = 1;
            
            RestoreIslandAfterNotification();
        }, TimeSpan.FromMilliseconds(500));
    }

    
    
    
    
    public void ShowNotificationOverlay(string left, string subject, string teacher, string time)
    {
        
        NotificationMaskBg.IsOpened = false;
        DispatcherTimer.RunOnce(() => NotificationMask.IsVisible = false, TimeSpan.FromMilliseconds(500));

        
        
        
        NotificationOverlayLeft.Text = left;
        NotificationOverlaySubject.Text = subject;
        NotificationOverlayTeacher.Text = teacher;
        NotificationOverlayTeacher.IsVisible = teacher.Length > 0;
        NotificationOverlayTime.Text = time;
        NotificationOverlay.IsVisible = true;
        NotificationOverlay.Opacity = 0;
        DispatcherTimer.RunOnce(StartNotificationTrack, TimeSpan.FromMilliseconds(500));

        var fade = new Animation
        {
            Delay = TimeSpan.FromMilliseconds(500),
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = Easing.Parse("0.25, 1, 0.5, 1"),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
            },
        };
        _ = fade.RunAsync(NotificationOverlay);
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

        
        visual.CenterPoint = new System.Numerics.Vector3((float)target / 2, 20f, 0f);

        
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
