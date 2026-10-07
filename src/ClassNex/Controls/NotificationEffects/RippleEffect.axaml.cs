using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace ClassNex.Controls.NotificationEffects;

/// <summary>
/// 1:1 移植自 CI（ClassIsland/Controls/NotificationEffects/RippleEffect.axaml.cs）。
/// 重要通知的全局水波纹：从一个中心点放出一个填满全屏的圆，放大 + 淡出。
/// </summary>
public partial class RippleEffect : UserControl, INotificationEffectControl
{
    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<RippleEffect, IBrush?>(
            nameof(Fill));

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public static readonly StyledProperty<double> CenterXProperty = AvaloniaProperty.Register<RippleEffect, double>(
        nameof(CenterX));

    public double CenterX
    {
        get => GetValue(CenterXProperty);
        set => SetValue(CenterXProperty, value);
    }

    public static readonly StyledProperty<double> CenterYProperty = AvaloniaProperty.Register<RippleEffect, double>(
        nameof(CenterY));

    public double CenterY
    {
        get => GetValue(CenterYProperty);
        set => SetValue(CenterYProperty, value);
    }

    public static readonly StyledProperty<double> EllipseSizeProperty = AvaloniaProperty.Register<RippleEffect, double>(
        nameof(EllipseSize));

    public double EllipseSize
    {
        get => GetValue(EllipseSizeProperty);
        set => SetValue(EllipseSizeProperty, value);
    }

    private PixelPoint CenterPoint { get; }

    /// <summary>无参构造：仅用于 XAML 设计期（消除 AVLN3001 警告）；运行时请用带中心点的构造。</summary>
    public RippleEffect() : this(default)
    {
    }

    public RippleEffect(PixelPoint center, IBrush? brush = null)
    {
        CenterPoint = center;
        Fill = brush ?? (Application.Current!.TryFindResource("AccentFillColorDefaultBrush", out var v) ? v as IBrush : null);
        InitializeComponent();
        EllipseMain.IsVisible = true;
    }

    public async void Play()
    {
        // 计算到达四个顶点的距离，取其最大值作为圆的最大半径。
        var (cx, cy) = this.PointToClient(CenterPoint);
        CenterX = cx;
        CenterY = cy;
        var topLevel = TopLevel.GetTopLevel(this);
        var r11 = Math.Sqrt(Math.Pow(cx, 2) + Math.Pow(cy, 2));
        var r12 = Math.Sqrt(Math.Pow(topLevel?.Width ?? 0 - cx, 2) + Math.Pow(cy, 2));
        var r21 = Math.Sqrt(Math.Pow(cx, 2) + Math.Pow(topLevel?.Height ?? 0 - cy, 2));
        var r22 = Math.Sqrt(Math.Pow(topLevel?.Width ?? 0 - cx, 2) + Math.Pow(topLevel?.Height ?? 0 - cy, 2));
        var r = Math.Ceiling(((List<double>)[r11, r12, r21, r22]).Max());

        EllipseSize = EllipseMain.Width = EllipseMain.Height = r * 2;
        var visual = ElementComposition.GetElementVisual(EllipseMain);
        if (visual == null)
        {
            return;
        }
        visual.Scale = new Vector3D(0, 0, 0);
        visual.Opacity = 1.0f;
        var compositor = visual.Compositor;
        var animationScale = compositor.CreateVector3DKeyFrameAnimation();
        animationScale.InsertKeyFrame(0f, new Vector3D(0, 0, 0));
        animationScale.InsertKeyFrame(1f, new Vector3D(1, 1, 1), new QuadraticEaseIn());
        animationScale.Duration = TimeSpan.FromMilliseconds(600);
        visual.StartAnimation(nameof(visual.Scale), animationScale);
        var animationOpacity = compositor.CreateScalarKeyFrameAnimation();
        animationOpacity.InsertKeyFrame(0f, 1f);
        animationOpacity.InsertKeyFrame(1f, 0f, new SineEaseIn());
        animationOpacity.Duration = TimeSpan.FromMilliseconds(600);
        visual.StartAnimation(nameof(visual.Opacity), animationOpacity);

        await Task.Delay(750);
        EffectCompleted?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? EffectCompleted;
}
