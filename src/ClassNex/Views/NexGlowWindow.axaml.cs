using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using ClassNex.Styles;

namespace ClassNex.Views;

public partial class NexGlowWindow : Window
{
    private readonly Stopwatch _clock = new();
    private readonly Action<TimeSpan> _drawFrame;
    private double _phaseStart, _phaseFrom, _nextDraw;
    private bool _closing, _closed, _framePending;
    private const double FullOpacity = .98;

    public bool IsRunning { get; private set; }
    public event Action? CancelRequested;
    public event Action? Stopped;

    public NexGlowWindow()
    {
        InitializeComponent();
        _drawFrame = DrawFrame;
        Root.Opacity = 0;
        Closed += (_, _) =>
        {
            _closed = true;
            IsRunning = false;
            _clock.Stop();
            Stopped?.Invoke();
        };
        PointerPressed += (_, _) => CancelRequested?.Invoke();
    }

    public void Start()
    {
        if (_closed || (IsRunning && !_closing)) return;
        if (!IsRunning) _clock.Restart();
        IsRunning = true;
        _closing = false;
        _phaseFrom = Root.Opacity;
        _phaseStart = _clock.Elapsed.TotalSeconds;
        Glow.DrawPetals = DesktopMotion.IsEnabled;
        if (!Glow.DrawPetals)
        {
            Glow.Intensity = .65;
            Root.Opacity = FullOpacity;
            return;
        }
        Glow.Intensity = 1;
        QueueFrame();
    }

    public void StopGlow()
    {
        if (_closed || _closing) return;
        if (!IsRunning || !IsVisible || !DesktopMotion.IsEnabled) { Close(); return; }
        _closing = true;
        _phaseFrom = Root.Opacity;
        _phaseStart = _clock.Elapsed.TotalSeconds;
        QueueFrame();
    }

    private void QueueFrame()
    {
        if (_framePending || _closed || !IsVisible) return;
        _framePending = true;
        RequestAnimationFrame(_drawFrame);
    }

    private void DrawFrame(TimeSpan frameTime)
    {
        _framePending = false;
        if (_closed || !IsRunning || !IsVisible) return;
        var now = _clock.Elapsed.TotalSeconds;
        var duration = _closing ? DesktopMotion.Exit : DesktopMotion.Enter;
        var progress = Math.Clamp((now - _phaseStart) / duration.TotalSeconds, 0, 1);
        if (_closing && progress >= 1) { Close(); return; }
        if (now >= _nextDraw)
        {
            var eased = (_closing ? DesktopMotion.ExitEase : DesktopMotion.EnterEase).Ease(progress);
            Root.Opacity = _phaseFrom + ((_closing ? 0 : FullOpacity) - _phaseFrom) * eased;
            Glow.Time = now;
            const double interval = 1d / 60;
            _nextDraw += (Math.Floor((now - _nextDraw) / interval) + 1) * interval;
        }
        QueueFrame();
    }

    public void UpdateWindowPos(Avalonia.Platform.Screen screen, double scale, bool useWorkingArea)
    {
        var area = useWorkingArea ? screen.WorkingArea : screen.Bounds;
        Position = new PixelPoint(area.X, area.Y);
        Width = area.Width * scale;
        Height = area.Height * scale;
    }
}
