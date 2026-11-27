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
    public double ScheduleWidth { get; set; } = 560;
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

    private Border? _wrapper;
    private IDisposable? _visibility;
    private WidgetPresentation? _appliedStyle;
    private HorizontalAlignment _originalAlignment;
    private double _surfaceOpacity;
    private string? _surfaceTheme;
    internal Control DisplayView => _wrapper ?? View;
    private bool _hasContent = true;
    internal bool AllowedInClassroom { get; private set; } = true;

    internal bool SetClassroomVisibility(bool allowed)
    {
        if (AllowedInClassroom == allowed) return false;
        AllowedInClassroom = allowed;
        View.IsVisible = allowed && _hasContent;
        return true;
    }

    protected void SetContentVisible(bool present)
    {
        if (_hasContent == present) return;
        _hasContent = present;
        View.IsVisible = present && AllowedInClassroom;
    }

    internal void ApplyPresentation()
    {
        static double Limit(double input, double min, double max, double fallback = 0) => double.IsFinite(input) ? Math.Clamp(input, min, max) : fallback;
        var style = Config.Presentation;
        var settings = AppServices.Settings;
        var theme = Avalonia.Application.Current?.ActualThemeVariant.Key?.ToString();
        if (Equals(_appliedStyle, style) && (style is null || (_surfaceOpacity == settings.BackgroundOpacity && _surfaceTheme == theme))) return;
        _appliedStyle = style;
        _surfaceOpacity = settings.BackgroundOpacity;
        _surfaceTheme = theme;
        if (style is null)
        {
            if (_wrapper is null) return;
            var parent = _wrapper.Parent as Panel;
            var index = parent?.Children.IndexOf(_wrapper) ?? -1;
            if (index >= 0) parent!.Children.RemoveAt(index);
            _visibility?.Dispose(); _visibility = null;
            _wrapper.Child = null; _wrapper = null;
            View.HorizontalAlignment = _originalAlignment;
            if (index >= 0) parent!.Children.Insert(index, View);
            return;
        }
        if (_wrapper is null)
        {
            var parent = View.Parent as Panel;
            var index = parent?.Children.IndexOf(View) ?? -1;
            if (index >= 0) parent!.Children.RemoveAt(index);
            _originalAlignment = View.HorizontalAlignment;
            _wrapper = new Border { Child = View, Tag = Config, ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
            _visibility = _wrapper.Bind(Visual.IsVisibleProperty, View.GetObservable(Visual.IsVisibleProperty));
            if (index >= 0) parent!.Children.Insert(index, _wrapper);
        }
        var wrapper = _wrapper;
        wrapper.Opacity = Limit(style.Opacity, 0, 1, 1);
        wrapper.Width = style.Width is { } width ? Limit(width, 0, 4096) : double.NaN;
        wrapper.MinWidth = style.Width is null && style.MinWidth is { } min ? Limit(min, 0, 4096) : 0;
        wrapper.MaxWidth = style.Width is null && style.MaxWidth is { } max ? Limit(max, wrapper.MinWidth, 4096, 4096) : double.PositiveInfinity;
        View.HorizontalAlignment = style.Alignment switch { "left" => HorizontalAlignment.Left, "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right, "stretch" => HorizontalAlignment.Stretch, _ => _originalAlignment };
        wrapper.Margin = style.Margin is [var l, var t, var r, var b] ? new Thickness(Limit(l, -160, 512), Limit(t, -160, 512), Limit(r, -160, 512), Limit(b, -160, 512)) : default;
        if (style.Background is { } text && Color.TryParse(text, out var color))
            wrapper.Background = new SolidColorBrush(color, Limit(style.BackgroundOpacity ?? _surfaceOpacity, 0, 1));
        else if (style.BackgroundOpacity is { } opacity) wrapper.Background = CiPalette.WithOpacity(CiPalette.IslandSurfaceBrush(), Limit(opacity, 0, 1));
        else wrapper.Background = null;
        wrapper.CornerRadius = style.CornerRadius is { } radius ? new CornerRadius(Limit(radius, 0, 512)) : default;
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
