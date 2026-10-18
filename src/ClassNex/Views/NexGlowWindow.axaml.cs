using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;

/// <summary>
/// 跑马灯窗口：全屏透明、置顶，屏幕四周一圈流动的彩色光带（参照 Apple 智能 / OPPO 小布的唤醒边缘光）。
///
/// 结构：四条独立光带（上/下/左/右），每条自带「彩虹 + 亮白彗星头」的循环渐变并沿自身方向流动。
/// 入场：从触发侧（唤醒条所在那侧）开始 —— 上下两条带同时朝对侧推进（宽 0→全宽），
///       到边后对侧那条带上下合拢（高 0→全高），像把光框一笔画出来；触发侧那条带渐显。
/// 退场：整体淡出后再隐藏。
/// </summary>
public partial class NexGlowWindow : Window
{
    private const double Fps = 60;
    private const double StripThickness = 16;

    private readonly DispatcherTimer _timer;

    /// <summary>颜色流动的相位（0..1 循环，重复填充保证无缝）。</summary>
    private double _flow;

    /// <summary>入场进度（宽/高推进）。</summary>
    private double _draw;

    /// <summary>退场中。</summary>
    private bool _closing;

    private double _outroOpacity = 1;

    /// <summary>从哪一侧流出来（true=左，false=右；跟随唤醒条贴边设置）。</summary>
    public bool StartFromLeft { get; set; }

    private readonly LinearGradientBrush _topBrush = BuildStripBrush(true);
    private readonly LinearGradientBrush _bottomBrush = BuildStripBrush(true);
    private readonly LinearGradientBrush _leftBrush = BuildStripBrush(false);
    private readonly LinearGradientBrush _rightBrush = BuildStripBrush(false);

    public NexGlowWindow()
    {
        InitializeComponent();

        TopStrip.Background = _topBrush;
        BottomStrip.Background = _bottomBrush;
        LeftStrip.Background = _leftBrush;
        RightStrip.Background = _rightBrush;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Step();

        // 点屏幕任意位置 = 取消 AI 模式（所以这个窗口不能点击穿透）
        PointerPressed += (_, _) => CancelRequested?.Invoke();
    }

    /// <summary>
    /// 一条光带的渐变：彩虹色环 + 一段亮白彗星头；首尾同色 + Repeat 保证循环无缝。
    /// horizontal=true 时沿 X 轴流，false 沿 Y 轴流。
    /// </summary>
    private static LinearGradientBrush BuildStripBrush(bool horizontal)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(horizontal ? 0 : 0.5, horizontal ? 0.5 : 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(horizontal ? 1 : 0.5, horizontal ? 0.5 : 1, RelativeUnit.Relative),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };

        void Stop(double offset, Color color) =>
            brush.GradientStops.Add(new GradientStop(color, offset));

        Stop(0.00, Color.FromRgb(0x5A, 0x9C, 0xFF));  // 蓝
        Stop(0.08, Color.FromArgb(0xEA, 0xFF, 0xFF, 0xFF)); // 彗星头（近白）
        Stop(0.16, Color.FromRgb(0x9B, 0x6B, 0xFF));  // 紫
        Stop(0.38, Color.FromRgb(0xFF, 0x6B, 0xC8));  // 粉
        Stop(0.60, Color.FromRgb(0xFF, 0xA8, 0x5A));  // 橙
        Stop(0.80, Color.FromRgb(0x4F, 0xE3, 0xC8));  // 青
        Stop(1.00, Color.FromRgb(0x5A, 0x9C, 0xFF));  // 回到蓝（无缝）

        return brush;
    }

    /// <summary>开始：从触发侧画出光框（上下两边推进 → 对侧合拢），同时开始流动。</summary>
    public void Start()
    {
        _flow = 0;
        _draw = 0;
        _closing = false;
        _outroOpacity = 1;

        Root.Opacity = 1;

        // 触发侧那条带立刻贴边、其余从 0 开始画
        var fromLeft = StartFromLeft;
        TopStrip.HorizontalAlignment = fromLeft ? Avalonia.Layout.HorizontalAlignment.Left
                                                : Avalonia.Layout.HorizontalAlignment.Right;
        BottomStrip.HorizontalAlignment = TopStrip.HorizontalAlignment;

        TopStrip.Width = 0;
        BottomStrip.Width = 0;
        LeftStrip.Height = 0;
        RightStrip.Opacity = 0;

        // 贴合屏幕边（DIP）
        Width = GetScreenWidth();
        Height = GetScreenHeight();

        Step();
        _timer.Start();
    }

    private double GetScreenWidth() => Screens.Primary is { } s
        ? s.Bounds.Width / s.Scaling
        : Bounds.Width;

    private double GetScreenHeight() => Screens.Primary is { } s
        ? s.Bounds.Height / s.Scaling
        : Bounds.Height;

    /// <summary>停止：整体淡出后隐藏。</summary>
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
        // 退场：淡出
        if (_closing)
        {
            _outroOpacity -= 0.06;
            if (_outroOpacity <= 0.02)
            {
                Root.Opacity = 0;
                _closing = false;
                _timer.Stop();
                Hide();
            }
            else
            {
                Root.Opacity = _outroOpacity;
            }

            return;
        }

        var w = GetScreenWidth();
        var h = GetScreenHeight();

        // ===== 入场：先上下出现，再左右出现（严格分两段，样式不动） =====
        if (_draw < 1)
        {
            _draw += 1 / (1.1 * Fps);   // 整段约 1.1 秒
            if (_draw > 1)
                _draw = 1;

            // 阶段一（0 ~ 50%）：上下两条边从触发侧推进到对侧，完全走完
            var p1 = Math.Clamp(_draw / 0.5, 0, 1);
            var e1 = 1 - Math.Pow(1 - p1, 3);
            TopStrip.Width = w * e1;
            BottomStrip.Width = w * e1;

            // 阶段二（55% ~ 100%）：上下到位之后，左右两条边才出现（从中间向上下合拢 + 渐显）
            var p2 = Math.Clamp((_draw - 0.55) / 0.45, 0, 1);
            var e2 = 1 - Math.Pow(1 - p2, 3);
            LeftStrip.Height = h * e2;
            RightStrip.Height = h * e2;
            LeftStrip.Opacity = e2;
            RightStrip.Opacity = e2;
        }

        // ===== 流动：渐变沿带子方向滚动（Repeat 填充 → 无缝） =====
        _flow += 0.0035;
        if (_flow > 1)
            _flow -= 1;

        var o = _flow;
        _topBrush.StartPoint = new RelativePoint(o, 0.5, RelativeUnit.Relative);
        _topBrush.EndPoint = new RelativePoint(o + 1, 0.5, RelativeUnit.Relative);
        _bottomBrush.StartPoint = new RelativePoint(o, 0.5, RelativeUnit.Relative);
        _bottomBrush.EndPoint = new RelativePoint(o + 1, 0.5, RelativeUnit.Relative);

        _leftBrush.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
        _leftBrush.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);
        _rightBrush.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
        _rightBrush.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);

        // 呼吸：整体明暗轻微起伏
        var breath = 0.86 + 0.14 * Math.Sin(_flow * Math.PI * 2 * 6);
        Root.Opacity = (0.78 + 0.12 * breath) * Math.Min(1, _draw * 1.4);
    }

    /// <summary>用户点了屏幕任意位置（用来取消 AI 模式）。</summary>
    public event Action? CancelRequested;

    /// <summary>铺满某个屏幕（scale 传 1/dpiX 把物理像素换算成 DIP）。</summary>
    public void UpdateWindowPos(Avalonia.Platform.Screen screen, double scale, bool useWorkingArea)
    {
        var area = useWorkingArea ? screen.WorkingArea : screen.Bounds;
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width * scale;
        Height = area.Height * scale;
    }
}
