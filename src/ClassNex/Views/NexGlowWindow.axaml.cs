using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;

/// <summary>
/// 跑马灯窗口：全屏透明、置顶，屏幕四周一圈流动的彩色光带（参照 Apple 智能 / OPPO 小布的唤醒边缘光）。
///
/// 性能（之前卡顿的根因，已改掉）：
///   ① 不用 BlurEffect —— 全屏模糊极贵；改用 OpacityMask 的渐变做柔边，开销几乎为零；
///   ② 入场不改 Width/Height（会触发重排）—— 改用 RenderTransform 缩放（纯合成，不重排）。
///
/// 出场顺序（用户要求）：**先上下两条边**从触发侧推进 → **再左右两条边**合拢。
/// 之后四条带子上的「彩虹 + 亮白彗星头」持续流动。
/// </summary>
public partial class NexGlowWindow : Window
{
    private const double Fps = 60;

    private readonly DispatcherTimer _timer;

    /// <summary>颜色流动相位。</summary>
    private double _flow;

    /// <summary>入场进度（0..1）。</summary>
    private double _draw;

    private bool _closing;
    private double _outroOpacity = 1;

    /// <summary>从哪一侧流出来（true=左，false=右；跟随唤醒条贴边设置）。</summary>
    public bool StartFromLeft { get; set; }

    public NexGlowWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Step();

        // 点屏幕任意位置 = 取消 AI 模式（所以这个窗口不能点击穿透）
        PointerPressed += (_, _) => CancelRequested?.Invoke();
    }

    /// <summary>开始：先上下两边推进，再左右两边合拢，同时开始流动。</summary>
    public void Start()
    {
        _flow = 0;
        _draw = 0;
        _closing = false;
        _outroOpacity = 1;
        Root.Opacity = 1;

        // 缩放原点：上下两条边从触发侧长出去（贴右就从右往左长，贴左反之）
        TopHost.RenderTransformOrigin = new RelativePoint(StartFromLeft ? 0 : 1, 0.5, RelativeUnit.Relative);
        BottomHost.RenderTransformOrigin = TopHost.RenderTransformOrigin;

        // 左右两条边从中间向上下长开
        LeftHost.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        RightHost.RenderTransformOrigin = LeftHost.RenderTransformOrigin;

        SetScale(TopHost, x: 0, y: 1);
        SetScale(BottomHost, x: 0, y: 1);
        SetScale(LeftHost, x: 1, y: 0);
        SetScale(RightHost, x: 1, y: 0);
        LeftHost.Opacity = 0;
        RightHost.Opacity = 0;

        Step();
        _timer.Start();
    }

    private static void SetScale(Control host, double x, double y)
    {
        if (host.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = x;
            st.ScaleY = y;
        }
    }

    private static ScaleTransform ScaleOf(Control host) => (ScaleTransform)host.RenderTransform!;

    /// <summary>停止：淡出后隐藏。</summary>
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

        // ===== 入场：先上下（红箭头），再左右（蓝箭头） =====
        if (_draw < 1)
        {
            _draw += 1 / (1.15 * Fps);   // 整段约 1.15 秒
            if (_draw > 1)
                _draw = 1;

            // 阶段一（0 ~ 55%）：上下两条边从触发侧推到对侧
            var p1 = Math.Clamp(_draw / 0.55, 0, 1);
            var e1 = 1 - Math.Pow(1 - p1, 3);
            ScaleOf(TopHost).ScaleX = e1;
            ScaleOf(BottomHost).ScaleX = e1;

            // 阶段二（45% ~ 100%）：左右两条边从中间向上下合拢
            var p2 = Math.Clamp((_draw - 0.45) / 0.55, 0, 1);
            var e2 = 1 - Math.Pow(1 - p2, 3);
            ScaleOf(LeftHost).ScaleY = e2;
            ScaleOf(RightHost).ScaleY = e2;
            LeftHost.Opacity = e2;
            RightHost.Opacity = e2;
        }

        // ===== 流动：四色带上的渐变沿自身方向滚动（Repeat 无缝） =====
        _flow += 0.0032;
        if (_flow > 1)
            _flow -= 1;

        var o = _flow;

        foreach (var host in new[] { TopHost, BottomHost })
        {
            if (host.Children.Count > 0 && host.Children[0] is Border { Background: LinearGradientBrush b } && b.GradientStops.Count > 0)
            {
                b.StartPoint = new RelativePoint(o, 0.5, RelativeUnit.Relative);
                b.EndPoint = new RelativePoint(o + 1, 0.5, RelativeUnit.Relative);
            }
        }

        foreach (var host in new[] { LeftHost, RightHost })
        {
            if (host.Children.Count > 0 && host.Children[0] is Border { Background: LinearGradientBrush b } && b.GradientStops.Count > 0)
            {
                b.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
                b.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);
            }
        }

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
