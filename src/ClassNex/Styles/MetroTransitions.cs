using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Styles;

/// <summary>
/// Avalonia implementation of a Metro/WP8.1 turnstile page transition.
/// Avalonia has no CSS perspective, so the 3D rotation is approximated with
/// scaleX + skew around an off-page vertical hinge, while all motion stays
/// limited to RenderTransform and Opacity (no layout invalidation).
/// </summary>
public static class MetroTransitions
{
    private const double PageDuration = 420;
    private const double ChildDuration = 420;
    private const double CrossFadeDuration = 220;

    private static readonly List<Entry> Entries = new();
    private static readonly Stopwatch Clock = new();

    private static DispatcherTimer? _timer;
    private static Control? _from;
    private static Control? _to;
    private static Action? _completed;

    public static void Play(
        Control? from,
        Control to,
        bool forward,
        Action? completed = null,
        Control? header = null)
    {
        Finish();

        if (from is not null && ReferenceEquals(from, to))
            from = null;

        _from = from;
        _to = to;
        _completed = completed;
        Entries.Clear();

        if (from is not null)
            ResetPageVisual(from);
        ResetPageVisual(to);

        to.IsVisible = true;
        to.IsHitTestVisible = true;

        if (from is not null)
        {
            from.IsVisible = true;
            from.ZIndex = 1;
            AddFade(from, 1, 0);
        }

        to.ZIndex = 2;
        AddFade(to, 0, 1);

        var sequence = new List<Control>();
        if (header is not null)
            sequence.Add(header);
        sequence.AddRange(CollectChildren(to));

        // WP8 direction: forward = title -> items top-to-bottom;
        // backward = bottom item -> ... -> title.
        if (!forward)
            sequence.Reverse();

        for (var i = 0; i < sequence.Count; i++)
            AddChild(sequence[i], i * 35.0);

        Clock.Restart();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void AddFade(Control control, double startOpacity, double endOpacity)
    {
        var scale = new ScaleTransform(1, 1);
        var skew = new SkewTransform(0, 0);
        var shift = new TranslateTransform(0, 0);

        control.Opacity = startOpacity;

        Entries.Add(new Entry
        {
            Control = control,
            Scale = scale,
            Skew = skew,
            Shift = shift,
            StartScale = 1,
            EndScale = 1,
            StartScaleY = 1,
            EndScaleY = 1,
            StartX = 0,
            EndX = 0,
            StartY = 0,
            EndY = 0,
            StartOpacity = startOpacity,
            EndOpacity = endOpacity,
            StartSkew = 0,
            EndSkew = 0,
            StartSkewY = 0,
            EndSkewY = 0,
            Delay = 0,
            Duration = CrossFadeDuration,
            OriginalTransform = control.RenderTransform,
            OriginalOrigin = control.RenderTransformOrigin,
            OriginalOpacity = 1,
            KeepLeftOrigin = false,
        });
    }

    public static void PlayList(Panel panel, int direction = 1)
    {
        Finish();
        Entries.Clear();

        var index = 0;
        foreach (var child in panel.Children.OfType<Control>())
        {
            AddChild(child, index * 35.0);
            index++;
        }

        if (Entries.Count == 0)
            return;

        Clock.Restart();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void AddPage(
        Control control,
        double originX,
        double startScale,
        double endScale,
        double startX,
        double endX,
        double startOpacity,
        double endOpacity,
        double startSkew,
        double endSkew,
        double delay)
    {
        var scale = new ScaleTransform(startScale, 1);
        var skew = new SkewTransform(startSkew, 0);
        var shift = new TranslateTransform(startX, 0);
        var group = new TransformGroup { Children = { scale, skew, shift } };

        var entry = new Entry
        {
            Control = control,
            Scale = scale,
            Skew = skew,
            Shift = shift,
            StartScale = startScale,
            EndScale = endScale,
            StartScaleY = 1,
            EndScaleY = 1,
            StartX = startX,
            EndX = endX,
            StartOpacity = startOpacity,
            EndOpacity = endOpacity,
            StartSkew = startSkew,
            EndSkew = endSkew,
            StartSkewY = 0,
            EndSkewY = 0,
            Delay = delay,
            Duration = PageDuration,
            OriginalTransform = control.RenderTransform,
            OriginalOrigin = control.RenderTransformOrigin,
            OriginalOpacity = control.Opacity,
        };

        control.RenderTransformOrigin = new RelativePoint(originX, 0.5, RelativeUnit.Relative);
        control.RenderTransform = group;
        control.Opacity = startOpacity;
        Entries.Add(entry);
    }

    private static void AddChild(Control control, double delay)
    {
        var originalTransform = control.RenderTransform;
        var originalOrigin = control.RenderTransformOrigin;
        var originalOpacity = control.Opacity;

        var scale = new ScaleTransform(0.70, 1);
        var skew = new SkewTransform(0, 0);
        var shift = new TranslateTransform(-45, 0);

        // Fixed hinge: left edge, vertical center. No Y movement, no shear.
        control.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        control.RenderTransform = new TransformGroup { Children = { scale, shift } };
        control.Opacity = 0;

        Entries.Add(new Entry
        {
            Control = control,
            Scale = scale,
            Skew = skew,
            Shift = shift,
            StartScale = 0.70,
            EndScale = 1,
            StartScaleY = 1,
            EndScaleY = 1,
            StartX = -45,
            EndX = 0,
            StartY = 0,
            EndY = 0,
            StartOpacity = 0,
            EndOpacity = 1,
            StartSkew = 0,
            EndSkew = 0,
            StartSkewY = 0,
            EndSkewY = 0,
            Delay = delay,
            Duration = ChildDuration,
            OriginalTransform = originalTransform,
            OriginalOrigin = originalOrigin,
            OriginalOpacity = originalOpacity,
            KeepLeftOrigin = true,
        });
    }

    private static IEnumerable<Control> CollectChildren(Control page)
    {
        if (page is Panel panel)
            return panel.Children.OfType<Control>().Take(8);

        if (page is ContentControl { Content: Control content })
            return CollectChildren(content);

        if (page is Decorator { Child: Control child })
            return CollectChildren(child);

        return Array.Empty<Control>();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        var elapsed = Clock.Elapsed.TotalMilliseconds;
        var allDone = true;

        foreach (var entry in Entries)
        {
            var progress = Math.Clamp((elapsed - entry.Delay) / entry.Duration, 0, 1);
            if (progress < 1)
                allDone = false;

            var eased = Bezier(progress);
            entry.Scale.ScaleX = Lerp(entry.StartScale, entry.EndScale, eased);
            entry.Scale.ScaleY = Lerp(
                entry.StartScaleY == 0 ? entry.StartScale : entry.StartScaleY,
                entry.EndScaleY == 0 ? entry.EndScale : entry.EndScaleY,
                eased);
            entry.Skew.AngleX = Lerp(entry.StartSkew, entry.EndSkew, eased);
            entry.Skew.AngleY = Lerp(entry.StartSkewY, entry.EndSkewY, eased);
            entry.Shift.X = Lerp(entry.StartX, entry.EndX, eased);
            entry.Shift.Y = Lerp(entry.StartY, entry.EndY, eased);
            entry.Control.Opacity = Lerp(entry.StartOpacity, entry.EndOpacity, eased);
        }

        if (allDone)
            Finish();
    }

    private static void Finish()
    {
        _timer?.Stop();
        _timer = null;

        foreach (var entry in Entries)
        {
            entry.Control.RenderTransform = entry.OriginalTransform;
            entry.Control.RenderTransformOrigin = entry.KeepLeftOrigin
                ? new RelativePoint(0, 0.5, RelativeUnit.Relative)
                : entry.OriginalOrigin;
            entry.Control.Opacity = entry.OriginalOpacity;
        }

        if (_from is not null)
        {
            ResetPageVisual(_from);
            _from.IsVisible = false;
            _from.IsHitTestVisible = false;
            _from.ZIndex = 0;
        }

        if (_to is not null)
        {
            ResetPageVisual(_to);
            _to.IsVisible = true;
            _to.IsHitTestVisible = true;
            _to.ZIndex = 0;
        }

        Entries.Clear();

        var completed = _completed;
        _from = null;
        _to = null;
        _completed = null;
        completed?.Invoke();
    }

    private static void ResetPageVisual(Control page)
    {
        page.RenderTransform = null;
        page.RenderTransformOrigin = RelativePoint.Center;
        page.Opacity = 1;
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    private static double Bezier(double t)
    {
        // cubic-bezier(0, 0, 0.2, 1): exponential ease-out, no overshoot
        const double x1 = 0.0, y1 = 0.0, x2 = 0.2, y2 = 1.0;
        var low = 0.0;
        var high = 1.0;
        var u = t;

        for (var i = 0; i < 10; i++)
        {
            var x = 3 * (1 - u) * (1 - u) * u * x1
                    + 3 * (1 - u) * u * u * x2
                    + u * u * u;
            if (x < t)
                low = u;
            else
                high = u;
            u = (low + high) / 2;
        }

        return 3 * (1 - u) * (1 - u) * u * y1
               + 3 * (1 - u) * u * u * y2
               + u * u * u;
    }

    private sealed class Entry
    {
        public Control Control = null!;
        public ScaleTransform Scale = null!;
        public SkewTransform Skew = null!;
        public TranslateTransform Shift = null!;
        public double StartScale;
        public double EndScale;
        public double StartScaleY;
        public double EndScaleY;
        public double StartX;
        public double EndX;
        public double StartY;
        public double EndY;
        public double StartOpacity;
        public double EndOpacity;
        public double StartSkew;
        public double EndSkew;
        public double StartSkewY;
        public double EndSkewY;
        public double Delay;
        public double Duration;
        public bool KeepLeftOrigin;
        public ITransform? OriginalTransform;
        public RelativePoint OriginalOrigin;
        public double OriginalOpacity;
    }
}
