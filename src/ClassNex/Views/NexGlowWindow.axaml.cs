using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;












public partial class NexGlowWindow : Window
{
    private const double Fps = 60;

    private readonly DispatcherTimer _timer;

    private double _flow;
    private double _draw;
    private bool _closing;
    private double _outroOpacity = 1;

    
    public bool StartFromLeft { get; set; }

    private readonly LinearGradientBrush _topBrush = BuildStripBrush(true);
    private readonly LinearGradientBrush _bottomBrush = BuildStripBrush(true);

    
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

        Stop(0.00, Color.FromRgb(0x5A, 0x9C, 0xFF));          
        Stop(0.08, Color.FromArgb(0xEA, 0xFF, 0xFF, 0xFF));    
        Stop(0.16, Color.FromRgb(0x9B, 0x6B, 0xFF));          
        Stop(0.38, Color.FromRgb(0xFF, 0x6B, 0xC8));          
        Stop(0.60, Color.FromRgb(0xFF, 0xA8, 0x5A));          
        Stop(0.80, Color.FromRgb(0x4F, 0xE3, 0xC8));          
        Stop(1.00, Color.FromRgb(0x5A, 0x9C, 0xFF));          

        return brush;
    }

    
    public void Start()
    {
        _flow = 0;
        _draw = 0;
        _closing = false;
        _outroOpacity = 1;
        Root.Opacity = 1;

        var fromLeft = StartFromLeft;

        
        TopStrip.HorizontalAlignment = fromLeft ? Avalonia.Layout.HorizontalAlignment.Left
                                                : Avalonia.Layout.HorizontalAlignment.Right;
        BottomStrip.HorizontalAlignment = TopStrip.HorizontalAlignment;
        TopStrip.Width = 0;
        BottomStrip.Width = 0;

        
        var h = GetScreenHeight();
        foreach (var s in new[] { RightTopStrip, RightBottomStrip, LeftTopStrip, LeftBottomStrip })
        {
            s.Height = h / 2;
            s.Opacity = 0;
            SetScaleY(s, 0);
        }

        
        
        
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

    
    public void StopGlow()
    {
        if (!_timer.IsEnabled)
        {
            Hide();
            return;
        }

        _closing = true;
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);   

    private void Step()
    {
        
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

        
        if (_draw < 1)
        {
            _draw += 1 / (1.35 * Fps);   
            if (_draw > 1)
                _draw = 1;

            var fromLeft = StartFromLeft;
            var triggerTop = fromLeft ? LeftTopStrip : RightTopStrip;
            var triggerBottom = fromLeft ? LeftBottomStrip : RightBottomStrip;
            var farTop = fromLeft ? RightTopStrip : LeftTopStrip;
            var farBottom = fromLeft ? RightBottomStrip : LeftBottomStrip;

            
            var e1 = Ease(Math.Clamp(_draw / 0.28, 0, 1));
            SetScaleY(triggerTop, e1);
            SetScaleY(triggerBottom, e1);
            triggerTop.Opacity = e1;
            triggerBottom.Opacity = e1;

            
            var e2 = Ease(Math.Clamp((_draw - 0.24) / 0.34, 0, 1));
            TopStrip.Width = GetScreenWidth() * e2;
            BottomStrip.Width = GetScreenWidth() * e2;

            
            var e3 = Ease(Math.Clamp((_draw - 0.54) / 0.46, 0, 1));
            SetScaleY(farTop, e3);
            SetScaleY(farBottom, e3);
            farTop.Opacity = e3;
            farBottom.Opacity = e3;
        }

        
        _flow += 0.0035;
        if (_flow > 1)
            _flow -= 1;

        var o = _flow;

        _topBrush.StartPoint = new RelativePoint(o, 0.5, RelativeUnit.Relative);
        _topBrush.EndPoint = new RelativePoint(o + 1, 0.5, RelativeUnit.Relative);
        _bottomBrush.StartPoint = new RelativePoint(o, 0.5, RelativeUnit.Relative);
        _bottomBrush.EndPoint = new RelativePoint(o + 1, 0.5, RelativeUnit.Relative);

        
        _rightTopBrush.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
        _rightTopBrush.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);
        _rightBottomBrush.StartPoint = new RelativePoint(0.5, o + 0.5, RelativeUnit.Relative);
        _rightBottomBrush.EndPoint = new RelativePoint(0.5, o + 1.5, RelativeUnit.Relative);

        _leftTopBrush.StartPoint = new RelativePoint(0.5, o, RelativeUnit.Relative);
        _leftTopBrush.EndPoint = new RelativePoint(0.5, o + 1, RelativeUnit.Relative);
        _leftBottomBrush.StartPoint = new RelativePoint(0.5, o + 0.5, RelativeUnit.Relative);
        _leftBottomBrush.EndPoint = new RelativePoint(0.5, o + 1.5, RelativeUnit.Relative);

        
        var breath = 0.88 + 0.12 * Math.Sin(_flow * Math.PI * 2 * 6);
        Root.Opacity = 0.82 * breath * Math.Min(1, 0.4 + _draw);
    }

    
    public event Action? CancelRequested;

    
    public void UpdateWindowPos(Avalonia.Platform.Screen screen, double scale, bool useWorkingArea)
    {
        var area = useWorkingArea ? screen.WorkingArea : screen.Bounds;
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width * scale;
        Height = area.Height * scale;
    }
}
