using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace ClassNex.WinUI;

internal sealed class OverlayPalette
{
    internal bool Dark { get; }
    internal bool HighContrast { get; }
    internal SolidColorBrush Ink { get; }
    internal SolidColorBrush SecondaryInk { get; }
    internal SolidColorBrush Accent { get; }
    internal SolidColorBrush AccentInk { get; }
    internal SolidColorBrush Edge { get; }
    internal SolidColorBrush SurfaceStroke { get; }
    internal SolidColorBrush Chip { get; }
    internal SolidColorBrush Selected { get; }
    internal SolidColorBrush Track { get; }
    internal SolidColorBrush MaskInk { get; }
    internal SolidColorBrush MaskSymbol { get; }
    internal SolidColorBrush Mask { get; }
    internal SolidColorBrush Island { get; }

    internal OverlayPalette(bool dark, bool highContrast, UISettings ui)
    {
        Dark = dark; HighContrast = highContrast;
        if (highContrast)
        {
            SolidColorBrush SystemColor(UIElementType type) => new(ui.UIElementColor(type));
            Ink = SecondaryInk = AccentInk = Edge = SurfaceStroke = SystemColor(UIElementType.WindowText);
            Island = Selected = Chip = Track = SystemColor(UIElementType.Window);
            Accent = Mask = SystemColor(UIElementType.Highlight);
            MaskInk = MaskSymbol = SystemColor(UIElementType.HighlightText);
            return;
        }
        var dictionaries = new List<ResourceDictionary>();
        CollectTheme(Application.Current.Resources, dark ? "Default" : "Light", dictionaries, 0);
        SolidColorBrush Neutral(string key, uint fallback)
        {
            foreach (var d in dictionaries)
                if (d.TryGetValue(key, out var resource) && resource is Color color) return new(color);
            return new(Argb(fallback));
        }
        Ink = Neutral("TextFillColorPrimary", dark ? 0xFFFFFFFFu : 0xE4000000u);
        SecondaryInk = Neutral("TextFillColorSecondary", dark ? 0xC5FFFFFFu : 0x9E000000u);
        Island = Neutral("SolidBackgroundFillColorBase", dark ? 0xFF202020u : 0xFFF3F3F3u);
        Selected = Neutral("SolidBackgroundFillColorQuarternary", dark ? 0xFF2C2C2Cu : 0xFFFFFFFFu);
        Chip = Neutral("ControlFillColorDefault", dark ? 0x0FFFFFFFu : 0xB3FFFFFFu);
        Edge = Neutral("ControlStrokeColorDefault", dark ? 0x12FFFFFFu : 0x0F000000u);
        SurfaceStroke = Neutral("SurfaceStrokeColorDefault", 0x66757575u);
        Track = Neutral("ControlStrongStrokeColorDefault", dark ? 0x8BFFFFFFu : 0x72000000u);
        Accent = new(ui.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1));
        AccentInk = new(ui.GetColorValue(dark ? UIColorType.AccentLight3 : UIColorType.AccentDark2));
        Mask = Selected; MaskInk = Ink; MaskSymbol = AccentInk;
    }

    internal SolidColorBrush? Custom(string? value)
    {
        if (HighContrast) return null;
        var hex = value?.Trim().TrimStart('#');
        if (hex is null || hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return null;
        return new SolidColorBrush(Color.FromArgb(hex.Length == 8 ? (byte)(n >> 24) : byte.MaxValue, (byte)(n >> 16), (byte)(n >> 8), (byte)n));
    }

    private static Color Argb(uint argb) => Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static void CollectTheme(ResourceDictionary resources, string theme, List<ResourceDictionary> result, int depth)
    {
        if (depth > 5) return;
        if (resources.ThemeDictionaries.TryGetValue(theme, out var value) && value is ResourceDictionary themed) result.Add(themed);
        for (var n = resources.MergedDictionaries.Count - 1; n >= 0; n--) CollectTheme(resources.MergedDictionaries[n], theme, result, depth + 1);
    }
}

public sealed partial class NativeOverlayWindow
{
    private static FontFamily? _textFont;

    private TextBlock Label(double size, Brush? color = null)
    {
        _textFont ??= AssetFont("HarmonyOS_Sans_SC_Regular.ttf", "HarmonyOS Sans SC", "Microsoft YaHei UI");
        var block = new TextBlock { FontFamily = _textFont, FontSize = size, Foreground = color ?? _palette.Ink,
            VerticalAlignment = VerticalAlignment.Center, IsTextScaleFactorEnabled = false };
        Typography.SetNumeralAlignment(block, Microsoft.UI.Xaml.FontNumeralAlignment.Tabular);
        return block;
    }

    private static FontFamily AssetFont(string file, string name, string fallback) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", file))
            ? new FontFamily($"ms-appx:///Assets/Fonts/{file}#{name}") : new FontFamily(fallback);

    private Border Chip(UIElement child, double pad = 8) => new()
    {
        Child = child, Background = _palette.Chip, BorderBrush = _palette.Edge, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8), Padding = new Thickness(pad, 2, pad, 2), MinHeight = 29,
        VerticalAlignment = VerticalAlignment.Center
    };
}
