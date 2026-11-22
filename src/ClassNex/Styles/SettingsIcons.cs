using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ClassNex.Styles;

internal static class SettingsIcons
{
    private static readonly ConcurrentDictionary<string, Lazy<Bitmap>> NavigationImages = new();

    public static FontFamily SymbolFont { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static Bitmap Navigation(string key)
    {
        var asset = key switch
        {
            "home" or "profile" or "appearance" or "window" or "widgets" or "notification" or "about" or "debug" or "account" => key,
            "basic" or "general" => "basic",
            "nex" or "ai" => "nex",
            _ => "settings"
        };
        return NavigationImages.GetOrAdd(asset, static name => new Lazy<Bitmap>(() =>
        {
            using var stream = AssetLoader.Open(new Uri($"avares://ClassNex/Assets/SettingsIcons/{name}.png"));
            return new Bitmap(stream);
        })).Value;
    }

    public static string WidgetGlyph(string type) => type switch
    {
        "text" => "\uE8D2",
        "divider" => "\uE738",
        "schedule" => "\uE8C0",
        "date" => "\uE787",
        "clock" => "\uE121",
        "nextclass" => "\uE893",
        "countdown" => "\uE916",
        _ => "\uE71D"
    };
}
