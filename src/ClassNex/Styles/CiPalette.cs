using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ClassNex.Styles;
















public static class CiPalette
{
    

    
    public static readonly Color CardBackground = Colors.Black;

    
    public const double CardOpacity = 0.5;

    
    public const double CardCornerRadius = 8;

    

    
    public static readonly Color Secondary = Color.Parse("#7FFFD4");

    
    public static readonly Color OverlaySubtle = Color.Parse("#48000000");

    
    public static readonly Color OverlayMedium = Color.Parse("#66000000");

    
    public static readonly Color NeutralDark = Color.Parse("#333333");

    
    public static readonly Color Highlight = Color.Parse("#F4EF74");

    public static readonly Color AvatarSurfaceLight = Color.Parse("#D6D6D6");

    public static readonly Color AvatarSurfaceDark = Color.Parse("#505050");

    

    
    
    
    
    public static IBrush AccentBrush()
    {
        if (TryResource("AccentFillColorDefaultBrush", out var brush))
            return brush;

        return new SolidColorBrush(Colors.SlateBlue);
    }

    
    
    
    
    
    
    
    
    
    public static IBrush CurrentLessonMaskBrush()
    {
        
        if (TryResource("ListViewItemBackgroundSelected", out var selected))
            return selected;

        
        var dark = IsDarkTheme();
        return dark
            ? new SolidColorBrush(Colors.White, 0.08)
            : new SolidColorBrush(Colors.Black, 0.05);
    }

    
    public static CornerRadius LessonCornerRadius()
    {
        
        if (Application.Current is { } app &&
            app.TryFindResource("ControlCornerRadius", out var value) &&
            value is CornerRadius radius)
        {
            return radius;
        }

        
        return new CornerRadius(4);
    }

    
    
    
    
    public static IBrush SkeletonBrush()
    {
        return IsDarkTheme()
            ? new SolidColorBrush(Colors.White, 0.16)
            : new SolidColorBrush(Colors.Black, 0.07);
    }

    
    
    
    
    
    
    public static IBrush ProgressTrackBrush()
    {
        var dark = IsDarkTheme();
        return dark
            ? new SolidColorBrush(Colors.White, 0.18)
            : new SolidColorBrush(Colors.Black, 0.18);
    }

    
    
    
    
    
    
    
    public static bool IsDarkTheme()
    {
        if (TryResource("TextFillColorPrimaryBrush", out var brush) && brush is ISolidColorBrush solid)
        {
            var c = solid.Color;
            var luma = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luma > 0.5;
        }

        return true;
    }

    
    
    
    
    
    
    
    
    
    
    
    
    public static IBrush IslandBorderBrush()
    {
        var dark = IsDarkTheme();

        
        var top = dark ? Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x52, 0x00, 0x00, 0x00);
        var bottom = dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1E, 0x00, 0x00, 0x00);

        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(top, 0.33),
                new GradientStop(bottom, 1.0),
            },
        };
    }

    
    
    
    
    
    public static IBrush SelectionBrush()
    {
        if (TryResource("SystemControlHighlightListAccentMediumLowBrush", out var brush))
            return brush;

        if (TryResource("AccentFillColorSecondaryBrush", out var secondary))
            return secondary;

        return AccentBrush();
    }

    
    public static IBrush OnAccentBrush()
    {
        if (TryResource("TextOnAccentFillColorPrimaryBrush", out var brush))
            return brush;

        return Brushes.White;
    }

    
    public static IBrush SurfaceBrush(string key, double fallbackOpacity = 0.2)
    {
        if (TryResource(key, out var brush))
            return brush;

        return new SolidColorBrush(NeutralDark, fallbackOpacity);
    }

    public static IBrush TimeLineBreakBrush()
    {
        if (TryResource("TextFillColorPrimaryBrush", out var brush) && brush is ISolidColorBrush solid)
            return new SolidColorBrush(solid.Color, 0.45);

        return new SolidColorBrush(NeutralDark, 0.45);
    }

    public static IBrush PrimaryTextBrush()
    {
        if (TryResource("TextFillColorPrimaryBrush", out var brush))
            return brush;

        return Brushes.White;
    }

    public static IBrush AvatarSurfaceBrush() =>
        new SolidColorBrush(IsDarkTheme() ? AvatarSurfaceDark : AvatarSurfaceLight);

    public static IBrush FocusStrokeBrush()
    {
        if (TryResource("FocusStrokeColorOuterBrush", out var brush))
            return brush;

        return AccentBrush();
    }

    public static IBrush FocusStrokeInnerBrush()
    {
        if (TryResource("FocusStrokeColorInnerBrush", out var brush))
            return brush;

        return IsDarkTheme() ? Brushes.Black : Brushes.White;
    }

    public static IBrush TimeLineSeparatorBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(20, 0, RelativeUnit.Relative),
            SpreadMethod = GradientSpreadMethod.Repeat,
            Transform = new RotateTransform(45),
        };
        brush.GradientStops.Add(new GradientStop(Colors.DarkGray, 0));
        brush.GradientStops.Add(new GradientStop(Colors.DarkGray, 0.5));
        brush.GradientStops.Add(new GradientStop(Colors.Gray, 0.5));
        brush.GradientStops.Add(new GradientStop(Colors.Gray, 1));
        return brush;
    }

    public static IBrush TimeLineActionBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(20, 0, RelativeUnit.Relative),
            SpreadMethod = GradientSpreadMethod.Repeat,
            Transform = new RotateTransform(45),
        };
        brush.GradientStops.Add(new GradientStop(Colors.Green, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Green, 0.5));
        brush.GradientStops.Add(new GradientStop(Colors.LimeGreen, 0.5));
        brush.GradientStops.Add(new GradientStop(Colors.LimeGreen, 1));
        return brush;
    }

    

    
    public static readonly IBrush OnCardBrush = Brushes.White;

    
    public static readonly IBrush CardBrush = new SolidColorBrush(CardBackground, CardOpacity);

    
    public static IBrush OnCardSecondary(double opacity = 0.82) =>
        new SolidColorBrush(Colors.White, opacity);

    public static bool TryResource(string key, out IBrush brush)
    {
        brush = Brushes.Transparent;

        try
        {
            var app = Application.Current;
            if (app is null)
                return false;

            
            
            
            if (app.TryFindResource(key, app.ActualThemeVariant, out var value) && value is IBrush themed)
            {
                brush = themed;
                return true;
            }

            if (app.TryFindResource(key, out var fallback) && fallback is IBrush plain)
            {
                brush = plain;
                return true;
            }
        }
        catch
        {
            
        }

        return false;
    }
}
