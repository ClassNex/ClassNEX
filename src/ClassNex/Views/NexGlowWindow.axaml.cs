using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;

public partial class NexGlowWindow : Window
{
    private const double Fps = 45;

    private readonly DispatcherTimer _timer;

    private double _time;
    private bool _closing;
    private double _outroOpacity = 1;

    // Kept for compatibility with the edge-bar setting; petals always enter from all four edges.
    public bool StartFromLeft { get; set; }

    public bool IsRunning => _timer.IsEnabled;

    public NexGlowWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000 / Fps) };
        _timer.Tick += (_, _) => Step();
        Closed += (_, _) => _timer.Stop();

        PointerPressed += (_, _) => CancelRequested?.Invoke();
    }

    public void Start()
    {
        _time = 0;
        _closing = false;
        _outroOpacity = 1;
        Root.Opacity = 0.98;

        Width = GetScreenWidth();
        Height = GetScreenHeight();

        Glow.Time = 0;
        Glow.Intensity = 0;

        Step();
        _timer.Start();
    }

    private double GetScreenWidth() => Screens.Primary is { } s
        ? s.Bounds.Width / s.Scaling
        : Bounds.Width;

    private double GetScreenHeight() => Screens.Primary is { } s
        ? s.Bounds.Height / s.Scaling
        : Bounds.Height;

    public void StopGlow()
    {
        if (!_timer.IsEnabled)
        {
            CompleteStop();
            return;
        }

        _closing = true;
    }

    private void Step()
    {
        if (_closing)
        {
            _outroOpacity -= 0.06;
            if (_outroOpacity <= 0.02)
            {
                CompleteStop();
            }
            else
            {
                Root.Opacity = _outroOpacity;
            }

            return;
        }

        _time += 1 / Fps;

        Glow.Time = _time;
        Glow.Intensity = Math.Min(1, _time * 3.4);
        Root.Opacity = 0.98;
    }

    public event Action? CancelRequested;

    public event Action? Stopped;

    private void CompleteStop()
    {
        Root.Opacity = 0;
        _closing = false;
        _timer.Stop();
        Glow.Intensity = 0;
        Glow.Time = 0;
        Width = 1;
        Height = 1;
        Hide();
        Stopped?.Invoke();
        Close();
    }

    public void UpdateWindowPos(Avalonia.Platform.Screen screen, double scale, bool useWorkingArea)
    {
        var area = useWorkingArea ? screen.WorkingArea : screen.Bounds;
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width * scale;
        Height = area.Height * scale;
    }
}
