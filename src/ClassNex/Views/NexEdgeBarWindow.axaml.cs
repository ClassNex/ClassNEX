using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ClassNex.Views;

/// <summary>
/// 吸附在屏幕左/右侧的半透明小白条：点一下等同于说「嘿 NEX」（提示音 + 跑马灯 + 浮窗翻页听你说话）。
/// 窗口本身透明无边框、置顶、不抢焦点、不进任务栏。
/// </summary>
public partial class NexEdgeBarWindow : Window
{
    private const double BarWidth = 14;
    private const double BarHeight = 120;

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
            WakeRequested?.Invoke();
        };

        // 悬停时亮一点、宽一点（给个"可点"的反馈）
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
