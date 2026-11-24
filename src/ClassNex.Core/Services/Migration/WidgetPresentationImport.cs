using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

internal static class WidgetPresentationImport
{
    internal static WidgetPresentation? Read(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;
        bool On(string name) => node.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;
        double Number(string key, double fallback, double min, double max) => node.TryGetProperty(key, out var val) &&
            val.ValueKind == JsonValueKind.Number && val.TryGetDouble(out var n) && double.IsFinite(n) ? Math.Clamp(n, min, max) : fallback;
        double? Width(string kind, double fallback) => On("Is" + kind + "WidthEnabled") ? Number(kind + "Width", fallback, 0, 4096) : null;
        var alignment = node.TryGetProperty("HorizontalAlignment", out var h) ? h.ToString().ToLowerInvariant() switch
            { "1" or "left" => "left", "2" or "center" => "center", "3" or "right" => "right", _ => null } : null;
        var margin = On("IsCustomMarginEnabled") ? new[] { "Left", "Top", "Right", "Bottom" }.Select(side => Number("Margin" + side, 0, -160, 512)).ToArray() : null;
        var bg = On("IsCustomBackgroundColorEnabled") && node.TryGetProperty("BackgroundColor", out var v) ? ClassIslandColor(v) : null;
        var result = new WidgetPresentation(Width("Fixed", 200), Width("Min", 100), Width("Max", 300), alignment, margin, bg,
            On("IsCustomBackgroundOpacityEnabled") ? Number("BackgroundOpacity", .5, 0, 1) : null,
            On("IsCustomCornerRadiusEnabled") ? Number("CustomCornerRadius", 8, 0, 512) : null, Number("Opacity", 1, 0, 1));
        return result == new WidgetPresentation() ? null : result;
    }

    internal static string? ClassIslandColor(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && value.GetString() is { } text && text.StartsWith('#') && text.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0)
            return text.Length switch { 9 => $"#{text[7..]}{text[1..7]}".ToUpperInvariant(), 7 => text.ToUpperInvariant(), _ => null };
        if (value.ValueKind != JsonValueKind.Object) return null;
        byte Channel(string channel, byte fallback) => value.TryGetProperty(channel, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetByte(out var b) ? b : fallback;
        if (!value.TryGetProperty("R", out _) || !value.TryGetProperty("G", out _) || !value.TryGetProperty("B", out _)) return null;
        return $"#{Channel("A", 255):X2}{Channel("R", 0):X2}{Channel("G", 0):X2}{Channel("B", 0):X2}";
    }
}
