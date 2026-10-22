using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;

namespace ClassNex.Widgets;


public sealed class WidgetContext
{
    public required AppSettings Settings { get; init; }

    public required ScheduleProfile Profile { get; init; }

    public required TodaySummary Today { get; init; }

    public required DateTime Now { get; init; }

    
    public string CountdownText { get; init; } = "";
}





public abstract class WidgetBase
{
    
    
    
    
    protected const double CiSecondary = 14;

    
    protected const double CiBody = 16;

    
    protected const double CiEmphasized = 18;

    
    protected const double CiLarge = 20;

    public WidgetConfig Config { get; init; } = new();

    
    public abstract string Type { get; }

    
    public Control View { get; protected set; } = new Panel();

    
    public abstract void Refresh(WidgetContext ctx);

    
    
    
    
    protected double Size(double baseSize) =>
        baseSize * Math.Clamp(Config.FontScale, 0.5, 2.5);

    
    
    
    
    
    protected static IBrush White(double opacity = 1.0)
    {
        if (CiPalette.TryResource("TextFillColorPrimaryBrush", out var brush))
        {
            if (opacity >= 1.0)
                return brush;

            if (brush is ISolidColorBrush solid)
                return new SolidColorBrush(solid.Color, Math.Clamp(solid.Opacity * opacity, 0, 1));

            return brush;
        }

        
        return new SolidColorBrush(Colors.White, opacity);
    }

    protected static TextBlock Text(
        string text,
        double fontSize,
        IBrush? foreground = null,
        FontWeight weight = FontWeight.Medium)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground ?? White(),
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };

        
        
        RenderOptions.SetTextRenderingMode(block, TextRenderingMode.Antialias);

        
        
        TextElement.SetFontFeatures(block, TabularFigures);
        return block;
    }

    
    protected static readonly FontFeatureCollection TabularFigures = new()
    {
        FontFeature.Parse("tnum"),
    };
}
