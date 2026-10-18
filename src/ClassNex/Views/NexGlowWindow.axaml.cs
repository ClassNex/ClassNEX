using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;

/// <summary>
/// 跑马灯窗口：全屏透明、置顶，屏幕四周一圈流动的彩色光带（参照 Apple 智能 / OPPO 小布的唤醒边缘光）。
///
/// 结构：上/下两条横带 + 左右各两条「半段」竖带（共 6 条），每条自带「彩虹 + 亮白彗星头」的循环渐变。
///
/// 出场顺序（用户指定的三段）：
///   ① 触发侧（唤醒条那侧）从**中间向上下**长开；
///   ② 上/下两条边从触发侧**推向对侧**；
///   ③ 对侧从**上下两端同时向中间**合拢（用户示意图的两个箭头）。
/// 竖直半段用 ScaleTransform 收缩/展开，纯合成、不重排。退场整体淡出。
/// </summary>
public partial class NexGlowWindow : Window
{
    private const double Fps = 60;

    private readonly DispatcherTimer _timer;

    private double _flow;
    private double _draw;
    private bool _closing;
    private double _outroOpacity = 1;

    /// <summary>从哪一侧流出来（true=左，false=右；跟随唤醒条贴边设置）。</summary>
    public bool StartFromLeft { get; set; }

    private readonly LinearGradientBrush _topBrush = BuildStripBrush(true);
    private readonly LinearGradientBrush _bottomBrush = BuildStripBrush(true);

    // 竖直方向四条半段：上段与下段的渐变相位差半程，视觉上首尾相接
    private readonly LinearGradientBrush _rightTopBrush = BuildStripBrush(false);
    private readonly LinearGradientBrush _rightBottomBrush = BuildStripBrush(false);
    private readonly LinearGradientBrush _leftTopBrush = BuildStripBrush(false);
    private readonly LinearGradientBrush _leftBottomBrush = BuildStripBrush(false);

    public NexGlowWindow()
    {
        InitializeComponent();

        TopStrip.Background = _topBrush;
        BottomStrip.Background = _bottomBrush;

        RightTopStrip.Background = _rightTopBrush;
        RightBottomStrip.Background = _rightBottomBrush;
        LeftTopStrip.Background = _leftTopBrush;
        LeftBottomStrip.Background = _leftBottomBrush;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Step();

        // 点屏幕任意位置 = 取消 AI 模式（所以这个窗口不能点击穿透）
        PointerPressed += (_, _) => CancelRequested?.Invoke();
    }

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

        Stop(0.00, Color.FromRgb(0x5A, 0x9C, 0xFF));          // 蓝
        Stop(0.08, Color.FromArgb(0xEA, 0xFF, 0xFF, 0xFF));    // 彗星头（近白）
        Stop(0.16, Color.FromRgb(0x9B, 0x6B, 0xFF));          // 紫
        Stop(0.38, Color.FromRgb(0xFF, 0x6B, 0xC8));          // 粉
        Stop(0.60, Color.FromRgb(0xFF, 0xA8, 0x5A));          // 橙
        Stop(0.80, Color.FromRgb(0x4F, 0xE3, 0xC8));          // 青
        Stop(1.00, Color.FromRgb(0x5A, 0x9C, 0xFF));          // 回到蓝（无缝）

        return brush;
    }

    /// <summary>开始：按「触发侧中间→上下 / 上下推向对侧 / 对侧两端→中间」的顺序画出光框。</summary>
    public void Start()
    {
        _flow = 0;
        _draw = 0;
        _closing = false;
        _outroOpacity = 1;
        Root.Opacity = 1;

        var fromLeft = StartFromLeft;

        // 横带：从触发侧往对侧延伸
        TopStrip.HorizontalAlignment = fromLeft ? Avalonia.Layout.HorizontalAlignment.Left
                                                : Avalonia.Layout.HorizontalAlignment.Right;
        BottomStrip.HorizontalAlignment = TopStrip.HorizontalAlignment;
        TopStrip.Width = 0;
        BottomStrip.Width = 0;

        // 竖带：高度取半屏，靠 ScaleY 展开
        var h = GetScreenHeight();
        foreach (var s in new[] { RightTopStrip, RightBottomStrip, LeftTopStrip, LeftBottomStrip })
        {
            s.Height = h / 2;
            s.Opacity = 0;
            SetScaleY(s, 0);
        }

        // 缩放原点：
        //   触发侧 = 从中间向上下 → 上半段原点在底、下半段原点在顶；
        //   对侧   = 从上下两端向中间 → 上半段原点在顶、下半段原点在底。
        var triggerTop = fromLeft ? LeftTopStrip : RightTopStrip;
        var triggerBottom = fromLeft ? LeftBottomStrip : RightBottomStrip;
        var farTop = fromLeft ? RightTopStrip : LeftTopStrip;
        var farBottom = fromLeft ? RightBottomStrip : LeftBottomStrip;

        triggerTop.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);
        triggerBottom.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        farTop.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        farBottom.RenderTransformOrigin = new RelativePoint(0.5, 1, RelativeUnit.Relative);

        Width = GetScreenWidth();
        Height = h;

        Step();
        _timer.Start();
    }

    private double GetScreenWidth() => Screens.Primary is { } s
        ? s.Bounds.Width / s.Scaling
        : Bounds.Width;

    private double GetScreenHeight() => Screens.Primary is { } s
        ? s.Bounds.Height / s.Scaling
        : Bounds.Height;

    private static ScaleTransform ScaleOf(Control c) => (ScaleTransform)c.RenderTransform!;

    private static void SetScaleY(Control c, double v) => ScaleOf(c).ScaleY = v;

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

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);   // cubic ease-out

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

        // ===== 入场（三段，样式不动） =====
        if (_draw < 1)
        {
            _draw += 1 / (1.35 * Fps);   // 整段约 1.35 秒
            if (_draw > 1)
                _draw = 1;

            var fromLeft = StartFromLeft;
            var triggerTop = fromLeft ? LeftTopStrip : RightTopStrip;
            var triggerBottom = fromLeft ? LeftBottomStrip : RightBottomStrip;
            var farTop = fromLeft ? RightTopStrip : LeftTopStrip;
            var farBottom = fromLeft ? RightBottomStrip : LeftBottomStrip;

            // ① 触发侧：从中间向上下长开（0 ~ 28%）
            var e1 = Ease(Math.Clamp(_draw / 0.28, 0, 1));
            SetScaleY(triggerTop, e1);
            SetScaleY(triggerBottom, e1);
            triggerTop.Opacity = e1;
            triggerBottom.Opacity = e1;

            // ② 上/下：从触发侧推向对侧（24% ~ 58%）
            var e2 = Ease(Math.Clamp((_draw - 0.24) / 0.34, 0, 1));
            TopStrip.Width = GetScreenWidth() * e2;
            BottomStrip.Width = GetScreenWidth() * e2;

            // ③ 对侧：上下两端同时向中间合拢（54% ~ 100%）
            var e3 = Ease(Math.Clamp((_draw - 0.54) / 0.46, 0, 1));
            SetScaleY(farTop, e3);
            SetScaleY(farBottom, e3);
            farTop.Opacity = e3;
            farBottom.Opacity = e3;
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

        // 上半段相位 o，下半段相位 o+0.5：两段拼起来像一整条连续光带
        _rightTopBrush.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
        _rightTopBrush.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);
        _rightBottomBrush.StartPoint = new RelativePoint(0.5, o + 0.5, RelativeUnit.Relative);
        _rightBottomBrush.EndPoint = new RelativePoint(0.5, o + 1.5, RelativeUnit.Relative);

        _leftTopBrush.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
        _leftTopBrush.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);
        _leftBottomBrush.StartPoint = new RelativePoint(0.5, o + 0.5, RelativeUnit.Relative);
        _leftBottomBrush.EndPoint = new RelativePoint(0.5, o + 1.5, RelativeUnit.Relative);

        // ===== 呼吸 =====
        var breath = 0.88 + 0.12 * Math.Sin(_flow * Math.PI * 2 * 6);
        Root.Opacity = 0.82 * breath * Math.Min(1, 0.4 + _draw);
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
