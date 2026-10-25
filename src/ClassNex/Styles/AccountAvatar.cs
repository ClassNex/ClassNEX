using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClassNex.Styles;

public static class AccountAvatar
{
    public static void Apply(Border target, string name, string? imagePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
            {
                using var stream = File.OpenRead(imagePath);
                var bitmap = new Bitmap(stream);
                target.Child = null;
                target.Background = new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                };
            }
            else
            {
                var size = target.Width > 0 ? target.Width : 48;
                target.Background = CiPalette.AvatarSurfaceBrush();
                target.Child = new TextBlock
                {
                    Text = Initial(name),
                    Foreground = CiPalette.PrimaryTextBrush(),
                    FontSize = Math.Max(13, size * 0.34),
                    FontWeight = FontWeight.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            if (CiPalette.TryResource("AccentFillColorDefaultBrush", out var brush) &&
                brush is ISolidColorBrush solid)
                target.BorderBrush = new SolidColorBrush(solid.Color);
        }
        catch
        {
        }
    }

    public static string Initial(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
            return "?";

        return name.Substring(0, 1).ToUpperInvariant();
    }
}
