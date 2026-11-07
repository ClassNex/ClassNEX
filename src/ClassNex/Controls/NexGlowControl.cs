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
            var color = PetalColors[i % PetalColors.Length];
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
                Color = color,
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
                DrawPetal(context, x, y, size * 2.1, rotation, petal.Color, 0.18);
                DrawPetal(context, x, y, size, rotation, petal.Color, 1.0);
            }
        }
    }

    private static void DrawEdgeGlow(DrawingContext context, double w, double h, double intensity)
    {
        const double depth = 48;
        const int rings = 44;
        var white = Color.FromRgb(0xFF, 0xF4, 0xF8);
        var pink = Color.FromRgb(0xF0, 0x8A, 0xA3);

        using (context.PushOpacity(intensity))
        {
            for (var i = 0; i < rings; i++)
            {
                var t = (i + 0.5) / rings;
                var d = t * depth;
                var alpha = (1 - t) * (1 - t) * 0.20;
                var color = Lerp(white, pink, t * 0.9);
                var rect = new Rect(1.5 + d, 1.5 + d, w - 3 - d * 2, h - 3 - d * 2);
                if (rect.Width <= 0 || rect.Height <= 0)
                    break;

                DrawRounded(
                    context,
                    rect,
                    2.6,
                    Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B));
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

    private static void DrawRounded(DrawingContext context, Rect rect, double thickness, Color color)
    {
        var pen = new Pen(
            new SolidColorBrush(color),
            thickness,
            lineCap: PenLineCap.Round,
            lineJoin: PenLineJoin.Round);
        context.DrawRectangle(null, pen, rect, 12, 12);
    }

    private static void DrawPetal(
        DrawingContext context,
        double cx,
        double cy,
        double size,
        double rotation,
        Color color,
        double alpha)
    {
        var cos = Math.Cos(rotation);
        var sin = Math.Sin(rotation);

        Point Transform(double lx, double ly)
        {
            var px = lx * size;
            var py = (ly + 0.5) * size * 1.35;
            return new Point(
                cx + px * cos - py * sin,
                cy + px * sin + py * cos);
        }

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(Transform(0, 0), true);
            ctx.CubicBezierTo(
                Transform(-0.55, -0.22),
                Transform(-0.48, -0.82),
                Transform(-0.10, -1));
            ctx.CubicBezierTo(
                Transform(-0.04, -0.86),
                Transform(0.04, -0.86),
                Transform(0.10, -1));
            ctx.CubicBezierTo(
                Transform(0.48, -0.82),
                Transform(0.55, -0.22),
                Transform(0, 0));
            ctx.EndFigure(true);
        }

        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(alpha * 255, 0, 255),
            color.R,
            color.G,
            color.B));

        context.DrawGeometry(brush, null, geometry);
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
        public Color Color;
    }
}
