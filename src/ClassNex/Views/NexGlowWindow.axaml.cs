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

    public NexGlowWindow()
    {
        InitializeComponent();

        GlowOuter.BorderBrush = _outerBrush;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Step();

        // 点屏幕任意位置 = 取消 AI 模式（所以这个窗口不能点击穿透）
        PointerPressed += (_, _) => CancelRequested?.Invoke();
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

    /// <summary>开始播放跑马灯。</summary>
    public void Start()
    {
        _phase = 0;
        _breath = 0;
        Step();
        _timer.Start();
    }

    /// <summary>停止并隐藏。</summary>
    public void StopGlow()
    {
        _timer.Stop();
        Hide();
    }

    private void Step()
    {
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
