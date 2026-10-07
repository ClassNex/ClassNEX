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

/// <summary>
/// 主界面：悬浮在桌面上的组件容器（对应 ClassIsland 的「主界面」）。
/// 显示哪些内容由「应用设置 → 主界面组件」决定。
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _hoverTimer;
    private readonly List<WidgetBase> _widgets = new();

    private IntPtr _hwnd;

    /// <summary>当前淡化目标（避免过渡动画被反复重启）。</summary>
    private double _hoverFadeTarget = 1.0;

    private static readonly Easing IslandWidthEasing = Easing.Parse("0.65, 0, 0.35, 1.0");

#if DEBUG
    private string _hoverDebugRect = "";
    private string _hoverDebugCursor = "";
#endif

    /// <summary>CI GridContentRoot 的 Margin="12 0"（岛宽 = 内容宽 + 两侧各 12）。</summary>
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

        // 每秒刷新，用于时钟与倒计时
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

        // 鼠标移入淡化：CI 用 RawInput 事件即时判定；本应用开启鼠标穿透收不到鼠标消息，
        // 故用 30ms 高频轮询光标位置近似 CI 的即时响应（淡化本身仍是 CI 的 100ms 线性过渡）
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _hoverTimer.Tick += (_, _) => UpdateHoverFade();
        _hoverTimer.Start();

        // 某些全屏窗口会抢走置顶，失焦后重新声明
        Deactivated += (_, _) => EnforcePlacement();

        // 背景岛宽度跟随内容：CI 的 BackgroundWidth 会做 300ms 过渡，
        // 所以内容变宽/变窄时，背景岛平滑追上，而不是硬跳。（只订阅一次）
        // ★ CI：BackgroundWidth = GridWrapper.Bounds.Width —— 是**包含 GridContentRoot 的
        //   Margin="12 0"** 的宽度（即内容宽 + 两侧各 12），否则文字会顶到岛边缘、没有内边距。
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

        // 每次显示都播放 CI 同款淡入（只订阅一次）
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && IsVisible)
                PlayFadeInAnimation();
        };

        // 主题切换（深色 ↔ 浅色）时重建组件：组件文字/画刷是「创建时按当前主题取色」的，
        // 不重建就会保持旧主题的颜色（用户反馈的「切主题字体不变黑白」）。
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ApplySettings();
                RebuildWidgets();
                RefreshWidgets();
            });
        }

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

        // ================= 1:1 CI 的岛（MainWindowLine 的 .line-background 样式）=================
        // 整体缩放：CI RootLayoutTransformControl 的 ScaleTransform = Settings.Scale（1.9）。
        // 岛内所有尺寸都用 CI 的原值（18/14/16/20 字号、40 高、12 边距、8 圆角……），
        // 由这个变换统一放大 —— 不再在组件里到处乘缩放。
        var scale = Math.Clamp(s.EffectiveScale, 0.4, 5.0);
        if (RootScale.LayoutTransform is ScaleTransform rootScale)
        {
            rootScale.ScaleX = scale;
            rootScale.ScaleY = scale;
        }

        // CI .line-background：Background=SolidBackgroundFillColorSecondaryBrush、
        // BorderBrush=ControlElevationBorderBrush、BorderThickness=1、CornerRadius=RadiusX(8)、
        // BoxShadow="0 4 8 2 #48000000"、Opacity=BackgroundOpacity（0.5）、Height=IslandContainerHeight(40)
        // 回滚到主题资源取色（浅/深色都跟主题走，和 CI 一致），不要再自己判深浅色
        IslandBackground.Background = CiPalette.SurfaceBrush("SolidBackgroundFillColorSecondaryBrush", s.BackgroundOpacity);
        IslandBackground.BorderBrush = CiPalette.IslandBorderBrush();
        IslandBackground.Opacity = Math.Clamp(s.BackgroundOpacity, 0.05, 1.0);
        IslandBackground.Height = 40;

        // 窗口尺寸（SizeToContent）跟随内容；背景岛宽度单独做补间动画
        if (CardContent.Bounds.Width > 0)
            SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);

        // 组件排列方向（CI 的一行横向排列；组件之间无间距，纵向时整岛加高，每行 40 高）
        var vertical = s.Orientation == LayoutOrientation.Vertical;
        WidgetHost.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        WidgetHost.Spacing = 0;
        if (vertical)
            IslandBackground.Height = 40 * Math.Max(1, _widgets.Count) + (_editMode ? 30 : 0);
        else
            IslandBackground.Height = 40 + (_editMode ? 30 : 0);

        // 编辑模式的底部工具条（CI 底部栏：添加组件 / 组件设置 / 完成）
        EditBarHost.IsVisible = _editMode;
        if (_editMode)
            BuildEditBar();

        // 窗口固定为整屏宽（CI 的做法）：内容宽度变化（剩余时间每秒变、课间切换……）时
        // 窗口本身不缩放、也不重新居中，只有岛在窗口内平滑变化 —— 否则会「一抖一抖」。
        var primary = Screens.Primary;
        if (primary is not null)
        {
            var screenScaling = primary.Scaling <= 0 ? 1.0 : primary.Scaling;
            Width = primary.WorkingArea.Width / screenScaling;
        }

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

        // ⚠️ 这里**不能**启用 CI 的 WrapPanelResizingAnimationAssist（隐式 Offset 动画）：
        // 岛的宽度是「布局立刻跳到目标宽 + composition Scale.X 补间放大」实现的
        // （见 SetIslandWidth —— Avalonia 的 Width 过渡在 LayoutTransformControl 里会卡布局）。
        // 若组件再加一层 300ms 的 Offset 补间，组件会被画在「旧位置 × 未完成的缩放」上，
        // 与邻居文字叠在一起（用户录屏里文本组件输入时 周三10/07 与 111111 相互重影）。
        // 只保留岛的宽度生长动画即可，组件位置直接跟随布局。
        WrapPanelResizingAnimationAssist.SetIsResizingAnimationEnabled(WidgetHost, false);

        foreach (var config in AppServices.Widgets.Widgets.Where(w => w.IsEnabled).OrderBy(w => w.Order))
        {
            var widget = WidgetFactory.Create(config);
            if (widget is null)
                continue;

            _widgets.Add(widget);

            if (_editMode)
            {
                // 编辑模式（照 CI）：每个组件都是独立的一项 —— 组件上方挂它自己的工具条
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

        // 编辑模式末尾的「＋ 添加组件」（CI 同位置是「＋ 新主界面行」）
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
                // 单个组件异常不影响整体
            }
        }
    }

    private void OnDataChanged() => Dispatcher.UIThread.Post(RefreshWidgets);

#if DEBUG
    /// <summary>
    /// 布局自检（仅调试）：CLASSNEX_VERIFY_LAYOUT=1 时每秒把窗口/背景岛/底部进度条/
    /// 课表当前项（剩余时间文本与倒计时胶囊）的实测尺寸写到 _verify_layout.log。
    /// </summary>
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
            // 自检失败忽略
        }
    }
#endif

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

        // CI 同款的「出现动画」：窗口真正显示后才开始（订阅 IsVisible 会在首帧渲染前触发，动画白跑）。
        // 顺序：① 先显示「骨架占位」（组件还没加载出来时）② 组件就绪后换成真实内容并 250ms 淡入。
        ShowSkeleton();
        if (Environment.GetEnvironmentVariable("CLASSNEX_VERIFY_SKELETON") == "1")
        {
            // 调试：保持骨架不切换，方便截图/核对样式
        }
        else
        {
            DispatcherTimer.RunOnce(HideSkeleton, TimeSpan.FromMilliseconds(380));
        }

        if (double.IsNaN(IslandBackground.Width))
            IslandBackground.Width = 0;
        SetIslandWidth(CardContent.Bounds.Width + IslandContentMargin * 2);
    }

    /// <summary>
    /// 启动占位骨架：组件还没加载出来时先显示占位块（一小块 + 一长块），
    /// 岛宽会跟着骨架宽度生长（CardContent.SizeChanged 驱动）。
    /// </summary>
    private void ShowSkeleton()
    {
        var brush = CiPalette.SkeletonBrush();
        SkeletonBlock1.Background = brush;
        SkeletonBlock2.Background = brush;
        SkeletonHost.IsVisible = true;
        WidgetHost.IsVisible = false;
    }

    /// <summary>组件就绪：收起骨架、显示真实内容并播放 CI 同款 250ms 淡入。</summary>
    private void HideSkeleton()
    {
        if (!SkeletonHost.IsVisible)
            return;

        SkeletonHost.IsVisible = false;
        WidgetHost.IsVisible = true;
        PlayFadeInAnimation();
    }

    /// <summary>浮窗岛的屏幕矩形（物理像素），供灵动通知胶囊定位（对照 CI MainWindowLine 取 GridWrapper 的做法）。</summary>
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

    /// <summary>浮窗岛中心的屏幕坐标（物理像素），供重要通知水波纹定位（对照 CI MainWindowLine.GetCenter）。</summary>
    public PixelPoint? GetIslandCenterOnScreen()
    {
        if (IslandBackground is not { } island || !island.IsLoaded)
            return null;

        return island.PointToScreen(new Point(island.Bounds.Width / 2, island.Bounds.Height / 2));
    }

    /// <summary>
    /// 重要通知遮罩：岛上显示大字提示（对照 CI 的 MaskContent + FluentTheme/Styles.axaml 的
    /// :mask-in 动画 —— 文字 Opacity 0→1（Delay 0.26s / 0.25s / 0.25,1,0.5,1）
    /// + Scale 1.1→1.0（Delay 0.26s / 0.75s / 0.25,1,0.5,1））。
    /// </summary>
    public void ShowNotificationMask(string text)
    {
        NotificationMaskText.Text = text;
        NotificationMask.IsVisible = true;

        // CI：SlantedMaskControl 斜切条纹打开（IsOpened=true，两边→中间，360ms/120ms 交错）
        NotificationMaskBg.IsOpened = true;

        // 文字按用户要求用黑色（强调色底上的黑字，深浅色模式一致）。
        // 挂在遮罩根的 TextElement.Foreground 上，继承到图标与文字；避免被其它样式覆盖。
        Avalonia.Controls.Documents.TextElement.SetForeground(NotificationMask, Brushes.Black);
        NotificationMaskText.Foreground = Brushes.Black;

        // CI :mask-in（FluentTheme/Styles.axaml）：
        //   Opacity 0→1：Delay 0.26s / 0.25s / 0.25,1,0.5,1
        //   ScaleX/Y 1.1→1.0：Delay 0.26s / 0.75s / 0.25,1,0.5,1
        // 用属性动画驱动 XAML 的 ScaleTransform（RenderTransformOrigin=50%,50% 生效）。
        // 之前用 composition 缩放，布局完成前 visual.Size=0 → 中心点错位 → 文字「跑一下」。
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

    /// <summary>隐藏通知遮罩（对照 CI 的 :mask-out —— 文字 Opacity 1→0 0.2s + 条纹收起）。</summary>
    public void HideNotificationMask()
    {
        // CI：SlantedMaskControl 斜切条纹收起（IsOpened=false，中间→两边，360ms+120ms≈480ms）
        NotificationMaskBg.IsOpened = false;

        // CI :mask-out —— 文字 Opacity 1→0（0.2s）
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

        // 等条纹收完（480ms）再隐藏整个遮罩 —— 之前 220ms 就藏了，结束动画「闪一下就没了」
        DispatcherTimer.RunOnce(() => NotificationMask.IsVisible = false, TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// 面具之后的 Overlay 阶段（对照 CI 的 ClassOffOverlay：下节课是…）：
    /// 文字换成下节课信息并淡入（条纹保持展开，保证黑字可读；CI 是收起条纹+白字，本项目按用户要求黑字）。
    /// </summary>
    public void ShowNotificationOverlay(string text)
    {
        NotificationMaskText.Text = text;

        var fade = new Animation
        {
            Delay = TimeSpan.FromMilliseconds(200),
            Duration = TimeSpan.FromMilliseconds(250),
            FillMode = FillMode.Forward,
            Easing = Easing.Parse("0.25, 1, 0.5, 1"),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
            },
        };
        _ = fade.RunAsync(NotificationMaskContent);
    }

    /// <summary>
    /// 预热斜切条纹动画（首次触发会 JIT + 编译几何，容易卡一下）：
    /// 启动后快速开-合一次，之后的提醒动画就顺滑了。面具保持隐藏，不会有可见闪烁。
    /// </summary>
    public void WarmNotificationMask()
    {
        NotificationMaskBg.IsOpened = true;
        DispatcherTimer.RunOnce(() => NotificationMaskBg.IsOpened = false, TimeSpan.FromMilliseconds(60));
    }

    /// <summary>
    /// 岛宽变化（CI BackgroundWidth DoubleTransition 0.300 / 0.65,0,0.35,1.0）。
    /// 布局宽度直接对齐内容；视觉宽度用 composition Scale 动画补间 ——
    /// Avalonia 的 Width 过渡在 LayoutTransformControl 内会卡住布局，不能用。
    /// </summary>
    private void SetIslandWidth(double target)
    {
        var previous = IslandBackground.Width;
        IslandBackground.Width = target;

        var visual = ElementComposition.GetElementVisual(IslandBackground);
        if (visual is null)
            return;

        // 以岛中心为锚点缩放（岛居中 → 与 CI 的背景宽动画一致，从中心对称展开/收缩）
        visual.CenterPoint = new System.Numerics.Vector3((float)target / 2, 20f, 0f);

        // 首次布局（previous 为 0/NaN）：从 0 展开（启动生长动画）；之后从旧宽/新宽 补间到 1
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

    /// <summary>
    /// 主界面显示时的淡入动画 —— 1:1 复刻 CI MainWindowLine.PlayFadeInAnimation：
    /// composition 标量动画 250ms、缓动 0.25,1,0.5,1（cubic-bezier），目标是 Opacity。
    /// </summary>
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

    /// <summary>应用「鼠标穿透」：开启后点击落到后方窗口，不再被主界面挡住。</summary>
    private void ApplyClickThrough()
    {
        if (_hwnd == IntPtr.Zero)
            _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

        // 编辑模式下必须禁用穿透，否则点不到组件工具条上的按钮
        WindowsOverlay.SetClickThrough(_hwnd, AppServices.Settings.IsClickThrough && !_editMode);

        // 与 CI 的 WindowFeatures.ToolWindow 等价：主界面不进 Alt+Tab 列表，
        // 这样按 Alt+Tab 切换窗口时浮窗不会被隐藏或关闭。
        WindowsOverlay.SetToolWindow(_hwnd, true);
    }

    /// <summary>
    /// 鼠标移入淡化（1:1 复刻 CI IsMouseInFadingEnabled）：
    ///   - 判定：光标是否落在主界面矩形内（CI MainWindowLine.GetMouseStatusByPos：
    ///     行矩形 × DPI × Scale；本项目无整体缩放，Bounds 即最终尺寸）
    ///   - 目标透明度：CI 写死 Opacity = 0.05（MainWindowLine.axaml 的 IsLineFaded 样式）
    ///   - 过渡：100ms 线性（MainWindow.axaml 里 HoverHost 的 DoubleTransition，与 CI Styles.axaml 一致）
    ///   - 响应：CI 靠 RawInput 事件即时判定；本应用开启鼠标穿透收不到鼠标消息，
    ///     故由 30ms 高频轮询近似（仅目标变化时才赋值，避免打断过渡动画）
    /// </summary>
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

        // 悬停判定用**岛**的屏幕矩形（CI 的 GetMouseStatusByPos 判的也是组件行自身，
        // 而不是整个窗口 —— 窗口现在是整屏宽，用窗口会把整条屏顶都算成悬停）
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

        // 与 CI 一致：编辑模式下不淡出（CI 的淡化样式带 [MainWindowInEditMode=False] 条件），
        // 否则鼠标移到浮窗上就看不到内容、也点不到工具条。
        var target = hovered && !_editMode
            ? Math.Clamp(AppServices.Settings.HoverOpacity, 0.0, 1.0)
            : 1.0;

        if (Math.Abs(_hoverFadeTarget - target) > 0.001)
        {
            _hoverFadeTarget = target;
            HoverHost.Opacity = target;
        }
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

        // 允许负值：窗口（透明）比屏幕宽时向两侧对称溢出，岛始终水平居中（同 CI 的整屏宽窗口）
        var x = wa.X + (wa.Width - widthPx) / 2;
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
