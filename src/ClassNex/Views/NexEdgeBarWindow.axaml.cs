using System;
using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using ClassNex.Services;

namespace ClassNex.Views;





public partial class NexEdgeBarWindow : Window
{
    private const double BarWidth = 14;
    private const double BarHeight = 120;

    private const double SweepLength = 36;      
    private const double SweepPeriodMs = 2200;  

    private static readonly IBrush IdleBackground = new SolidColorBrush(Color.FromArgb(0x5C, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush HoverBackground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush FlashBackground = new SolidColorBrush(Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush IdleBorder = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush HoverBorder = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

    
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
            Bar.Background = HoverBackground;
            Bar.BorderBrush = HoverBorder;
        };
        Bar.PointerExited += (_, _) =>
        {
            Bar.Background = IdleBackground;
            Bar.BorderBrush = IdleBorder;
        };

        Opened += (_, _) =>
        {
            ShowInTaskbar = false;
            ApplyToolWindowStyle();
            StartSweepAnimation();
        };
        Closed += (_, _) => StopSweepAnimation();
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty)
                return;

            if (IsVisible)
                Dispatcher.UIThread.Post(StartSweepAnimation, DispatcherPriority.Loaded);
            else
                StopSweepAnimation();
        };
    }

    private void StartSweepAnimation()
    {
        if (!IsVisible || ElementComposition.GetElementVisual(Sweep) is not { } visual)
            return;

        var compositor = visual.Compositor;
        var easing = new CubicEaseInOut();
        var move = compositor.CreateVector3KeyFrameAnimation();
        move.InsertKeyFrame(0f, new Vector3(0, (float)-SweepLength, 0), easing);
        move.InsertKeyFrame(0.5f, new Vector3(0, (float)(BarHeight + SweepLength), 0), easing);
        move.InsertKeyFrame(1f, new Vector3(0, (float)-SweepLength, 0), easing);
        move.Duration = TimeSpan.FromMilliseconds(SweepPeriodMs);
        move.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Offset), move);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0.35f, easing);
        fade.InsertKeyFrame(0.5f, 0.85f, easing);
        fade.InsertKeyFrame(1f, 0.35f, easing);
        fade.Duration = move.Duration;
        fade.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(visual.Opacity), fade);
    }

    private void StopSweepAnimation()
    {
        if (ElementComposition.GetElementVisual(Sweep) is not { } visual)
            return;

        visual.StopAnimation(nameof(visual.Offset));
        visual.StopAnimation(nameof(visual.Opacity));
    }

    private void ApplyToolWindowStyle()
    {
        if (TryGetPlatformHandle() is { } handle)
            WindowsOverlay.SetToolWindow(handle.Handle, true);
    }

    public override void Show()
    {
        ShowInTaskbar = false;
        base.Show();
        ApplyToolWindowStyle();
    }

    private void Flash()
    {
        Bar.Background = FlashBackground;
        DispatcherTimer.RunOnce(() =>
        {
            Bar.Background = HoverBackground;
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
