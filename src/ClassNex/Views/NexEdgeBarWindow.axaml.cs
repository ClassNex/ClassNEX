using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;

/// <summary>
/// 吸附在屏幕左/右侧的半透明小白条：点一下等同于说「嘿 NEX」（提示音 + 跑马灯 + 浮窗翻页听你说话）。
/// 参照 Apple 智能 / OPPO 小布：一条光带沿条身上下往复流动（彗星扫动）+ 轻微呼吸，窗口透明无边框、置顶、不抢焦点。
/// </summary>
public partial class NexEdgeBarWindow : Window
{
    private const double BarWidth = 14;
    private const double BarHeight = 120;

    private const double SweepLength = 36;      // 光带长度
    private const double SweepPeriodMs = 2200;  // 一趟扫动时长（往返各一次）

    private readonly DispatcherTimer _anim;
    private readonly Stopwatch _clock = new();
    private double _sweepY;                     // 光带当前 Y（在 0..BarHeight 之间往复）

    /// <summary>被点击（唤醒）。</summary>
    public event Action? WakeRequested;

    /// <summary>贴哪一侧。</summary>
    public bool IsLeftSide { get; set; } = false;

    public NexEdgeBarWindow()
    {
        InitializeComponent();

        Bar.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            Flash();
            WakeRequested?.Invoke();
        };

        // 悬停时亮一点（给个"可点"的反馈），光带也随之亮起
        Bar.PointerEntered += (_, _) =>
        {
            Bar.Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            Bar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
        };
        Bar.PointerExited += (_, _) =>
        {
            Bar.Background = new SolidColorBrush(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF));
            Bar.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
        };

        // 光带动画：DispatcherTimer 30ms 步进（约 33fps 的柔和扫动，足够顺、且确定性可靠）
        _anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _anim.Tick += (_, _) => StepSweep();
        _clock.Start();
        _anim.Start();
    }

    private void StepSweep()
    {
        // 往复：0 → 1 → 0，ease-in-out；光带在条身两端之外多走一段，看着像"从侧边流出来"
        var t = (_clock.ElapsedMilliseconds % SweepPeriodMs) / SweepPeriodMs;
        var pingPong = Math.Sin(t * Math.PI);            // 0→1→0
        var eased = 0.5 - 0.5 * Math.Cos(pingPong * Math.PI);  // smooth

        var travel = BarHeight + SweepLength * 2;         // 全程（两端各多出半条光带）
        _sweepY = -SweepLength + eased * travel;

        // 通过位移渲染（纯合成，不重排）
        if (Sweep.RenderTransform is TranslateTransform tt)
        {
            tt.Y = _sweepY;
        }
        else
        {
            Sweep.RenderTransform = new TranslateTransform(0, _sweepY);
        }

        // 呼吸 + 中间最亮、两端渐隐（光带透明度随位置变化）
        var edge = Math.Abs(pingPong - 0.5) * 2;         // 0=中间, 1=两端
        Sweep.Opacity = 0.35 + 0.5 * (1 - edge);
    }

    /// <summary>点击瞬间闪一下（光带拉满 + 条身变亮）。</summary>
    private void Flash()
    {
        Sweep.Opacity = 1.0;
        Bar.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        DispatcherTimer.RunOnce(() =>
        {
            Bar.Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        }, TimeSpan.FromMilliseconds(180));
    }

    /// <summary>按屏幕与贴边方向摆好位置（scale 传 1/dpiX 把物理像素换算成 DIP）。</summary>
    public void UpdateWindowPos(Avalonia.Platform.Screen screen, double scale)
    {
        var area = screen.WorkingArea;
        Width = BarWidth;
        Height = BarHeight;

        var x = IsLeftSide
            ? area.X
            : area.X + area.Width - (int)Math.Ceiling(BarWidth * screen.Scaling);

        var y = area.Y + (area.Height - (int)Math.Ceiling(BarHeight * screen.Scaling)) / 2;

        Position = new PixelPoint(x, y);
    }
}
