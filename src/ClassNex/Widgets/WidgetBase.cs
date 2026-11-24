using Avalonia;
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
    public double ScheduleWidth { get; init; } = 560;
}

public abstract class WidgetBase
{

    protected const double CiSecondary = 14;

    protected const double CiBody = 16;

    protected const double CiEmphasized = 18;

    protected const double CiLarge = 20;

    protected const double ChipHeight = 29;

    protected const double ChipRadius = 8;

    protected const double WidgetGap = 4;

    public WidgetConfig Config { get; init; } = new();

    public abstract string Type { get; }

    public Control View { get; protected set; } = new Panel();

    private Control? _displayView;
    internal Control DisplayView => _displayView ??= Config.Presentation is { } p ? Present(p) : View;

    private Control Present(WidgetPresentation style)
    {
        static double Limit(double input, double min, double max, double fallback = 0) => double.IsFinite(input) ? Math.Clamp(input, min, max) : fallback;
        var wrapper = new Border { Child = View, Opacity = Limit(style.Opacity, 0, 1, 1), VerticalAlignment = VerticalAlignment.Center };
        if (style.Width is { } fixedWidth) wrapper.Width = Limit(fixedWidth, 0, 4096);
        else
        {
            if (style.MinWidth is { } min) wrapper.MinWidth = Limit(min, 0, 4096);
            if (style.MaxWidth is { } max) wrapper.MaxWidth = Limit(max, wrapper.MinWidth, 4096, 4096);
        }
        if (style.Alignment is not null) View.HorizontalAlignment = style.Alignment switch
            { "left" => HorizontalAlignment.Left, "right" => HorizontalAlignment.Right, "center" => HorizontalAlignment.Center, _ => HorizontalAlignment.Stretch };
        if (style.Margin is [var l, var t, var r, var b]) wrapper.Margin = new Thickness(Limit(l, -160, 512), Limit(t, -160, 512), Limit(r, -160, 512), Limit(b, -160, 512));
        if (style.Background is { } text && Color.TryParse(text, out var color))
            wrapper.Background = new SolidColorBrush(color, Limit(style.BackgroundOpacity ?? AppServices.Settings.BackgroundOpacity, 0, 1));
        else if (style.BackgroundOpacity is { } opacity) wrapper.Background = CiPalette.WithOpacity(CiPalette.IslandSurfaceBrush(), Limit(opacity, 0, 1));
        if (style.CornerRadius is { } radius) wrapper.CornerRadius = new CornerRadius(Limit(radius, 0, 512));
        wrapper.ClipToBounds = true;
        wrapper.Bind(Visual.IsVisibleProperty, View.GetObservable(Visual.IsVisibleProperty));
        return wrapper;
    }

    public abstract void Refresh(WidgetContext ctx);

    protected double Size(double baseSize) =>
        baseSize * Math.Clamp(Config.FontScale, 0.5, 2.5);

    protected IBrush White(double opacity = 1.0)
    {
        if (Config.UseCustomFontColor && Config.FontColor is { } chosen && Color.TryParse(chosen, out var ink)) return new SolidColorBrush(ink, opacity);
        if (AppServices.Settings.OverlayForegroundColor is { } text && Color.TryParse(text, out var color))
            return new SolidColorBrush(color, opacity);
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

    protected TextBlock Text(
        string text,
        double fontSize,
        IBrush? foreground = null,
        FontWeight weight = FontWeight.Medium)
    {        var block = new TextBlock
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

    protected static Border Pill(
        Control content,
        IBrush? background = null,
        IBrush? borderBrush = null,
        double height = ChipHeight,
        double padding = 7,
        double radius = ChipRadius,
        Thickness? margin = null)
    {
        return new Border
        {
            Height = height,
            Padding = new Thickness(padding, 0),
            CornerRadius = new CornerRadius(radius),
            Background = background ?? CiPalette.ChipSurfaceBrush(),
            BorderBrush = borderBrush ?? CiPalette.ChipBorderBrush(),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? default,
            Child = content,
        };
    }

    protected static readonly FontFeatureCollection TabularFigures = new()
    {
        FontFeature.Parse("tnum"),
    };
}
