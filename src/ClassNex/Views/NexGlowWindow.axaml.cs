using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;

/// <summary>
/// 跑马灯窗口：全屏透明、置顶、点击穿透，屏幕四周一圈流动的彩色光带。
/// 参考苹果智能（Apple Intelligence / Siri）唤醒时的边缘辉光，用两圈渐变描边 + 模糊叠出来。
/// </summary>
public partial class NexGlowWindow : Window
{
    private const double Fps = 60;

    private readonly DispatcherTimer _timer;
    private double _phase;
    private double _breath;

    /// <summary>入场进度（0→1，从侧边展开 + 淡入）。</summary>
    private double _intro = 1;

    /// <summary>退场中（淡出后隐藏）。</summary>
    private bool _closing;

    /// <summary>从哪一侧吸出（true=左边缘，false=右边缘）。</summary>
    public bool StartFromLeft { get; set; }

    /// <summary>Root 上的缩放变换（XAML 里 RenderTransform 内的命名控件不生成字段，从这里取）。</summary>
    private ScaleTransform RootScale => (ScaleTransform)Root.RenderTransform!;

    /// <summary>渐变的色标（沿光带流动）。</summary>
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x5A, 0x9C, 0xFF),   // 蓝
        Color.FromRgb(0x9B, 0x6B, 0xFF),   // 紫
        Color.FromRgb(0xFF, 0x6B, 0xC8),   // 粉
        Color.FromRgb(0xFF, 0xA8, 0x5A),   // 橙
        Color.FromRgb(0x4F, 0xE3, 0xC8),   // 青
        Color.FromRgb(0x5A, 0x9C, 0xFF),   // 回到蓝（首尾同色，循环不跳）
    };

    private readonly LinearGradientBrush _outerBrush = BuildBrush();

    /// <summary>彗星头：一截亮白（轻微暖白），首尾全透明 —— 只有一小段亮斑，跟着彩虹走。</summary>
    private readonly LinearGradientBrush _cometBrush = BuildCometBrush();

    public NexGlowWindow()
    {
        InitializeComponent();

        GlowOuter.BorderBrush = _outerBrush;
        GlowComet.BorderBrush = _cometBrush;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Step();

        // 点屏幕任意位置 = 取消 AI 模式（所以这个窗口不能点击穿透）
        PointerPressed += (_, _) => CancelRequested?.Invoke();
    }

    private static LinearGradientBrush BuildCometBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        };

        // 彗星头：前 0.06 亮起、0.14 处渐隐，其余全透明（只占光带约 1/7 的一截亮斑）
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, 0xFF, 0xF6, 0xEE), 0.05));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.16));

        return brush;
    }

    private static LinearGradientBrush BuildBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };

        for (var i = 0; i < Palette.Length; i++)
            brush.GradientStops.Add(new GradientStop(Palette[i], i / (double)(Palette.Length - 1)));

        return brush;
    }

    /// <summary>开始播放跑马灯：从侧边「吸出」—— 光带由屏幕边缘向全屏展开 + 淡入（约 0.55 秒，减速曲线）。</summary>
    public void Start()
    {
        _phase = 0;
        _breath = 0;
        _closing = false;
        _intro = 0;

        // 入场起点：整体缩在触发那一侧的边缘（像从唤醒条里被吸出来）
        Root.Opacity = 0;
        RootScale.ScaleX = 0.55;
        RootScale.ScaleY = 0.55;
        Root.RenderTransformOrigin = new RelativePoint(
            StartFromLeft ? 0 : 1, 0.5, RelativeUnit.Relative);

        Step();
        _timer.Start();
    }

    /// <summary>停止：先淡出（约 0.25 秒）再隐藏，不再瞬间消失。</summary>
    public void StopGlow()
    {
        if (!_timer.IsEnabled)
        {
            Hide();
            return;
        }

        _closing = true;
    }

    private void Step()
    {
        // 退场：淡出，透明度到 0 后隐藏
        if (_closing)
        {
            Root.Opacity -= 0.06;
            if (Root.Opacity <= 0.02)
            {
                Root.Opacity = 0;
                _closing = false;
                _timer.Stop();
                Hide();
            }

            return;
        }

        // 入场：从侧边展开 + 淡入（0.55 秒，cubic ease-out）
        if (_intro < 1)
        {
            _intro += 1 / (0.55 * Fps);
            if (_intro > 1)
                _intro = 1;

            var e = 1 - Math.Pow(1 - _intro, 3);
            Root.Opacity = e;
            var s = 0.55 + 0.45 * e;
            RootScale.ScaleX = s;
            RootScale.ScaleY = s;
        }

        // 转速：用户反馈原来太快了 —— 从每帧 0.010 降到 0.0035（一圈约 4.8 秒）
        _phase += 0.0035;
        if (_phase > 1)
            _phase -= 1;

        _breath += 0.016;

        // 渐变端点绕屏幕中心转一圈：光带看起来就在沿着边缘流动
        var angle = _phase * Math.PI * 2;
        var dx = Math.Cos(angle) * 0.5;
        var dy = Math.Sin(angle) * 0.5;

        _outerBrush.StartPoint = new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative);
        _outerBrush.EndPoint = new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative);

        // 呼吸：整体明暗轻微起伏（0.72 ~ 1.0）—— 第一版的幅度
        var breath = 0.86 + 0.14 * Math.Sin(_breath);
        GlowOuter.Opacity = 0.75 * breath;

        // 彗星头：相位比彩虹超前约 1/12 圈 —— 亮头在前、彩虹尾在后
        var cAngle = (_phase + 0.085) * Math.PI * 2;
        var cdx = Math.Cos(cAngle) * 0.5;
        var cdy = Math.Sin(cAngle) * 0.5;

        _cometBrush.StartPoint = new RelativePoint(0.5 - cdx, 0.5 - cdy, RelativeUnit.Relative);
        _cometBrush.EndPoint = new RelativePoint(0.5 + cdx, 0.5 + cdy, RelativeUnit.Relative);

        // 彗星随呼吸同步明暗（比彩虹层略亮一个台阶）
        GlowComet.Opacity = 0.72 * breath;
    }

    /// <summary>用户点了屏幕任意位置（用来取消 AI 模式）。</summary>
    public event Action? CancelRequested;

    /// <summary>铺满某个屏幕（scale 传 1/dpiX，把物理像素换算成 DIP）。</summary>
    public void UpdateWindowPos(Avalonia.Platform.Screen screen, double scale, bool useWorkingArea)
    {
        var area = useWorkingArea ? screen.WorkingArea : screen.Bounds;
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width * scale;
        Height = area.Height * scale;
    }
}
