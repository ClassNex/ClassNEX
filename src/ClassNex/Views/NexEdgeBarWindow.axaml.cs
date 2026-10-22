using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;





public partial class NexEdgeBarWindow : Window
{
    private const double BarWidth = 14;
    private const double BarHeight = 120;

    private const double SweepLength = 36;      
    private const double SweepPeriodMs = 2200;  

    private readonly DispatcherTimer _anim;
    private readonly Stopwatch _clock = new();
    private double _sweepY;                     

    
    public event Action? WakeRequested;

    
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

        
        _anim = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _anim.Tick += (_, _) => StepSweep();
        _clock.Start();
        _anim.Start();
    }

    private void StepSweep()
    {
        
        var t = (_clock.ElapsedMilliseconds % SweepPeriodMs) / SweepPeriodMs;
        var pingPong = Math.Sin(t * Math.PI);            
        var eased = 0.5 - 0.5 * Math.Cos(pingPong * Math.PI);  

        var travel = BarHeight + SweepLength * 2;         
        _sweepY = -SweepLength + eased * travel;

        
        if (Sweep.RenderTransform is TranslateTransform tt)
        {
            tt.Y = _sweepY;
        }
        else
        {
            Sweep.RenderTransform = new TranslateTransform(0, _sweepY);
        }

        
        var edge = Math.Abs(pingPong - 0.5) * 2;         
        Sweep.Opacity = 0.35 + 0.5 * (1 - edge);
    }

    
    private void Flash()
    {
        Sweep.Opacity = 1.0;
        Bar.Background = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
        DispatcherTimer.RunOnce(() =>
        {
            Bar.Background = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
        }, TimeSpan.FromMilliseconds(180));
    }

    
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
