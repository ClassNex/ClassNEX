using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClassNex.Controls;

/// <summary>
/// Sakura petals entering from the screen edges and drifting inward.
/// </summary>
public sealed class NexGlowControl : Control
{
    public static readonly StyledProperty<double> TimeProperty =
        AvaloniaProperty.Register<NexGlowControl, double>(nameof(Time));

    public static readonly StyledProperty<double> IntensityProperty =
        AvaloniaProperty.Register<NexGlowControl, double>(nameof(Intensity), 1.0);

    private static readonly Color[] PetalColors =
    {
        Color.FromRgb(0xE9, 0x74, 0x96),
        Color.FromRgb(0xF0, 0x8A, 0xA3),
        Color.FromRgb(0xFF, 0xB3, 0xC8),
        Color.FromRgb(0xFF, 0xC9, 0xDA),
        Color.FromRgb(0xD9, 0x5C, 0x7C),
    };

    private static readonly Geometry PetalGeometry = CreatePetalGeometry();
    private static readonly IBrush[] PetalBrushes = CreatePetalBrushes();
    private static readonly Pen[] GlowPens = CreateGlowPens();
    private readonly List<Petal> _petals = new();

    static NexGlowControl()
    {
        AffectsRender<NexGlowControl>(TimeProperty, IntensityProperty);
    }

    public NexGlowControl()
    {
        var random = new Random(20261103);

        for (var i = 0; i < 150; i++)
        {
            _petals.Add(new Petal
            {
                Edge = random.Next(4),
                Offset = 0.02 + random.NextDouble() * 0.96,
                Distance = 0.05 + random.NextDouble() * 0.10,
                Drift = 10 + random.NextDouble() * 32,
                Phase = random.NextDouble(),
                Speed = 0.13 + random.NextDouble() * 0.15,
                Size = 12 + random.NextDouble() * 16,
                Rotation = random.NextDouble() * Math.PI * 2,
                Spin = -1.6 + random.NextDouble() * 3.2,
                Brush = PetalBrushes[i % PetalBrushes.Length],
            });
        }
    }

    public double Time
    {
        get => GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public double Intensity
    {
        get => GetValue(IntensityProperty);
        set => SetValue(IntensityProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 64 || h < 64)
            return;

        var intensity = Math.Clamp(Intensity, 0, 1);
        if (intensity <= 0)
            return;

        DrawEdgeGlow(context, w, h, intensity);

        var span = Math.Min(w, h);

        foreach (var petal in _petals)
        {
            var u = (Time * petal.Speed + petal.Phase) % 1.0;
            if (u <= 0.001 || u >= 0.999)
                continue;

            var fade = Math.Sin(u * Math.PI);
            if (fade <= 0.01)
                continue;

            double x;
            double y;
            double inX;
            double inY;

            switch (petal.Edge)
            {
                case 0: // top
                    x = w * petal.Offset;
                    y = -petal.Size;
                    inX = 0;
                    inY = 1;
                    break;
                case 1: // right
                    x = w + petal.Size;
                    y = h * petal.Offset;
                    inX = -1;
                    inY = 0;
                    break;
                case 2: // bottom
                    x = w * petal.Offset;
                    y = h + petal.Size;
                    inX = 0;
                    inY = -1;
                    break;
                default: // left
                    x = -petal.Size;
                    y = h * petal.Offset;
                    inX = 1;
                    inY = 0;
                    break;
            }

            var travel = span * petal.Distance * u;
            var sway = Math.Sin(u * Math.PI * 1.8 + petal.Phase * Math.PI * 2)
                       * petal.Drift * fade;

            x += inX * travel - inY * sway;
            y += inY * travel + inX * sway;

            var size = petal.Size * (0.82 + 0.18 * Math.Sin(u * Math.PI * 2 + petal.Phase * 4));
            var rotation = petal.Rotation + petal.Spin * Time;

            using (context.PushOpacity(fade * intensity))
            {
                DrawPetal(context, x, y, size * 2.1, rotation, petal.Brush, 0.18);
                DrawPetal(context, x, y, size, rotation, petal.Brush, 1.0);
            }
        }
    }

    private static void DrawEdgeGlow(DrawingContext context, double w, double h, double intensity)
    {
        const double depth = 48;
        using (context.PushOpacity(intensity))
        {
            for (var i = 0; i < GlowPens.Length; i++)
            {
                var t = (i + 0.5) / GlowPens.Length;
                var d = t * depth;
                var rect = new Rect(1.5 + d, 1.5 + d, w - 3 - d * 2, h - 3 - d * 2);
                if (rect.Width <= 0 || rect.Height <= 0)
                    break;

                context.DrawRectangle(null, GlowPens[i], rect, 12, 12);
            }
        }
    }

    private static Color Lerp(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * t),
            (byte)(from.G + (to.G - from.G) * t),
            (byte)(from.B + (to.B - from.B) * t));
    }

    private static void DrawPetal(
        DrawingContext context,
        double cx,
        double cy,
        double size,
        double rotation,
        IBrush brush,
        double alpha)
    {
        if (alpha <= 0.001)
            return;

        var transform = Matrix.CreateScale(size, size * 1.35)
                        * Matrix.CreateRotation(rotation)
                        * Matrix.CreateTranslation(cx, cy);

        using (context.PushOpacity(Math.Clamp(alpha, 0, 1)))
        using (context.PushTransform(transform))
            context.DrawGeometry(brush, null, PetalGeometry);
    }

    private static IBrush[] CreatePetalBrushes()
    {
        var brushes = new IBrush[PetalColors.Length];
        for (var i = 0; i < brushes.Length; i++)
            brushes[i] = new SolidColorBrush(PetalColors[i]);
        return brushes;
    }

    private static Pen[] CreateGlowPens()
    {
        const int rings = 32;
        var white = Color.FromRgb(0xFF, 0xF4, 0xF8);
        var pink = Color.FromRgb(0xF0, 0x8A, 0xA3);
        var pens = new Pen[rings];

        for (var i = 0; i < rings; i++)
        {
            var t = (i + 0.5) / rings;
            var alpha = (1 - t) * (1 - t) * 0.20;
            var color = Lerp(white, pink, t * 0.9);
            pens[i] = new Pen(
                new SolidColorBrush(Color.FromArgb(
                    (byte)Math.Clamp(alpha * 255, 0, 255),
                    color.R,
                    color.G,
                    color.B)),
                2.6,
                lineCap: PenLineCap.Round,
                lineJoin: PenLineJoin.Round);
        }

        return pens;
    }

    private static Geometry CreatePetalGeometry()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0.5), true);
            context.CubicBezierTo(
                new Point(-0.55, 0.28),
                new Point(-0.48, -0.32),
                new Point(-0.10, -0.5));
            context.CubicBezierTo(
                new Point(-0.04, -0.36),
                new Point(0.04, -0.36),
                new Point(0.10, -0.5));
            context.CubicBezierTo(
                new Point(0.48, -0.32),
                new Point(0.55, 0.28),
                new Point(0, 0.5));
            context.EndFigure(true);
        }

        return geometry;
    }

    private sealed class Petal
    {
        public int Edge;
        public double Offset;
        public double Distance;
        public double Drift;
        public double Phase;
        public double Speed;
        public double Size;
        public double Rotation;
        public double Spin;
        public IBrush Brush = Brushes.Transparent;
    }
}
