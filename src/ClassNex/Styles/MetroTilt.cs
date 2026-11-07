using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClassNex.Styles;

/// <summary>
/// Long-strip tilt behavior. The host transform is always around 50%,50%,
/// and the motion is driven by TransformOperationsTransition (compositor
/// transition), never by a per-frame timer. The outer control keeps layout;
/// an optional descendant named "TiltHost" receives the transform.
/// </summary>
public static class MetroTilt
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, TiltState> States = new();

    public static void Attach(Control control)
    {
        if (States.TryGetValue(control, out _))
            return;

        var state = new TiltState(control);
        States.Add(control, state);

        control.AddHandler(InputElement.PointerPressedEvent, state.OnPressed, RoutingStrategies.Tunnel, true);
        control.AddHandler(InputElement.PointerReleasedEvent, state.OnReleased, RoutingStrategies.Tunnel, true);
        control.AddHandler(InputElement.PointerCaptureLostEvent, state.OnReleased, RoutingStrategies.Tunnel, true);
    }

    private sealed class TiltState
    {
        private static readonly TimeSpan PressDuration = TimeSpan.FromMilliseconds(70);
        private static readonly TimeSpan ReleaseDuration = TimeSpan.FromMilliseconds(180);
        private static readonly SplineEasing Easing = new(new KeySpline(0.1, 0.9, 0.2, 1.0));

        private readonly Control _outer;
        private Control? _host;
        private ITransform? _originalTransform;
        private Transitions? _originalTransitions;
        private DispatcherTimer? _restoreTimer;
        private bool _pressed;

        public TiltState(Control outer) => _outer = outer;

        public void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_outer).Properties.IsLeftButtonPressed)
                return;

            _host = ResolveHost(_outer);
            if (_host.Bounds.Width <= 1 || _host.Bounds.Height <= 1)
                return;

            _restoreTimer?.Stop();
            _restoreTimer = null;

            if (!_pressed)
            {
                _originalTransform = _host.RenderTransform;
                _originalTransitions = _host.Transitions;
            }

            _pressed = true;

            var point = e.GetPosition(_host);
            var w = _host.Bounds.Width;
            var h = _host.Bounds.Height;
            var dx = Math.Clamp((point.X - w / 2) / (w / 2), -1, 1);
            var dy = Math.Clamp((point.Y - h / 2) / (h / 2), -1, 1);

            // Long-strip damping: limit the vertical edge displacement.
            var skewY = dx * (150.0 / Math.Max(w, 100.0));
            var skewX = -dy * 0.6;
            var translateX = dx * 1.5;
            var translateY = dy * 0.8;

            _host.Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = PressDuration,
                    Easing = Easing,
                },
            };

            _host.RenderTransform = TransformOperations.Parse(string.Format(
                CultureInfo.InvariantCulture,
                "translate({0:0.###}px, {1:0.###}px) scale(0.985) skew({2:0.###}deg, {3:0.###}deg)",
                translateX,
                translateY,
                skewX,
                skewY));
        }

        public void OnReleased(object? sender, PointerEventArgs e)
        {
            if (!_pressed || _host is null)
                return;

            _pressed = false;

            _host.Transitions = new Transitions
            {
                new TransformOperationsTransition
                {
                    Property = Visual.RenderTransformProperty,
                    Duration = ReleaseDuration,
                    Easing = Easing,
                },
            };

            _host.RenderTransform = TransformOperations.Parse("translate(0px, 0px) scale(1) skew(0deg, 0deg)");

            _restoreTimer?.Stop();
            _restoreTimer = new DispatcherTimer { Interval = ReleaseDuration + TimeSpan.FromMilliseconds(10) };
            _restoreTimer.Tick += (_, _) =>
            {
                _restoreTimer?.Stop();
                _restoreTimer = null;

                if (_host is null)
                    return;

                _host.RenderTransform = _originalTransform;
                _host.Transitions = _originalTransitions;
                _originalTransform = null;
                _originalTransitions = null;
            };
            _restoreTimer.Start();
        }

        private static Control ResolveHost(Control outer)
        {
            // Explicit inner host from the WinUI Strip Card template.
            if (outer.Name == "TiltHost")
                return outer;

            var named = outer.GetVisualDescendants()
                .OfType<Control>()
                .FirstOrDefault(c => c.Name == "TiltHost");
            return named ?? outer;
        }
    }
}
