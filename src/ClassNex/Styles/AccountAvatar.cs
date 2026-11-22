using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClassNex.Styles;

public static class AccountAvatar
{
    private sealed class Picture
    {
        public Bitmap? Bitmap;
        public string Path = "";
        public DateTime Modified;

        public void Clear()
        {
            Bitmap?.Dispose();
            Bitmap = null;
            Path = "";
        }
    }

    private static readonly ConditionalWeakTable<Border, Picture> Pictures = new();

    public static void Apply(Border target, string name, string? imagePath)
    {
        var picture = Pictures.GetValue(target, border =>
        {
            var current = new Picture();
            border.DetachedFromVisualTree += (_, _) =>
            {
                if (current.Bitmap is not null) border.Background = null;
                current.Clear();
            };
            return current;
        });
        if (!string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var modified = File.GetLastWriteTimeUtc(imagePath);
                if (picture.Bitmap is not null && picture.Path == imagePath && picture.Modified == modified) return;
                using var stream = File.OpenRead(imagePath);
                var bitmap = Bitmap.DecodeToWidth(stream, 160);
                target.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                target.Child = null;
                picture.Clear();
                picture.Bitmap = bitmap;
                picture.Path = imagePath;
                picture.Modified = modified;
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException) { }
        }
        target.Background = CiPalette.AvatarSurfaceBrush();
        picture.Clear();
        target.Child = new TextBlock
        {
            Text = Initial(name),
            Foreground = CiPalette.PrimaryTextBrush(),
            FontSize = Math.Max(13, (target.Width > 0 ? target.Width : 48) * 0.34),
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    public static string Initial(string? name) => string.IsNullOrWhiteSpace(name) ? "?" : StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
}
