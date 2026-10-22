using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClassNex.Views;






public sealed class FireworksView : Control
{
    private sealed class Particle
    {
        public double X, Y, Vx, Vy, Life, MaxLife, Size;
        public Color Color;
    }

    private readonly List<Particle> _particles = new();
    private readonly Random _random = new();
    private readonly DispatcherTimer _timer;
    private double _launchCooldown;
    private bool _running;

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x60, 0xCD, 0xFF),   
        Color.FromRgb(0xFF, 0xD1, 0x66),
        Color.FromRgb(0xFF, 0x8A, 0x80),
        Color.FromRgb(0xB4, 0xA0, 0xFF),
        Color.FromRgb(0x7F, 0xE8, 0xB0),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
    };

    public FireworksView()
    {
        ClipToBounds = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Step();
        AttachedToVisualTree += (_, _) => Start();
        DetachedFromVisualTree += (_, _) => Stop();
    }

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _particles.Clear();
        _launchCooldown = 0.2;
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    private void Step()
    {
        var dt = 0.016;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 1 || h <= 1)
            return;

        
        _launchCooldown -= dt;
        if (_launchCooldown <= 0)
        {
            _launchCooldown = 0.55 + _random.NextDouble() * 0.55;
            LaunchRocket(w, h);
        }

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vy += 220 * dt;      
            p.Vx *= 0.995;
            p.Life -= dt;

            if (p.Life <= 0 || p.Y > h + 40)
                _particles.RemoveAt(i);
        }

        InvalidateVisual();
    }

    
    private void LaunchRocket(double w, double h)
    {
        var x = w * (0.12 + _random.NextDouble() * 0.76);
        var apex = h * (0.12 + _random.NextDouble() * 0.3);
        var riseTime = 0.5 + _random.NextDouble() * 0.25;
        var vy = -(h - apex) / riseTime;
        var color = Palette[_random.Next(Palette.Length)];
        var explodeAt = h - (h - apex);

        
        var steps = 18;
        for (var i = 0; i < steps; i++)
        {
            var t = i / (double)steps * riseTime;
            var y = h - (h - apex) * (t / riseTime);
            _particles.Add(new Particle
            {
                X = x,
                Y = y,
                Vx = (0.4 - _random.NextDouble()) * 8,
                Vy = vy * 0.15,
                Life = 0.35 + (steps - i) * 0.012,
                MaxLife = 0.6,
                Size = 2.1,
                Color = color,
            });
        }

        
        var count = 70 + _random.Next(50);
        var speed = h * (0.30 + _random.NextDouble() * 0.22);
        for (var i = 0; i < count; i++)
        {
            var angle = i / (double)count * Math.PI * 2 + _random.NextDouble() * 0.06;
            var magnitude = speed * (0.55 + _random.NextDouble() * 0.45);
            var life = 0.85 + _random.NextDouble() * 0.85;
            _particles.Add(new Particle
            {
                X = x,
                Y = explodeAt,
                Vx = Math.Cos(angle) * magnitude,
                Vy = Math.Sin(angle) * magnitude,
                Life = life,
                MaxLife = life,
                Size = 1.6 + _random.NextDouble() * 1.9,
                Color = _random.NextDouble() < 0.18 ? Palette[_random.Next(Palette.Length)] : color,
            });
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        foreach (var p in _particles)
        {
            var t = Math.Clamp(p.Life / p.MaxLife, 0, 1);
            var alpha = (byte)(255 * t * t);          
            var brush = new SolidColorBrush(Color.FromArgb(alpha, p.Color.R, p.Color.G, p.Color.B));
            var r = p.Size * (0.4 + 0.6 * t);
            context.DrawEllipse(brush, null, new Point(p.X, p.Y), r, r);
        }
    }
}
