using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Styling;
using AvaloniaFluentUI.Media;
using AvaloniaFluentUI.Styling;
using System.Runtime.InteropServices;
using Windows.UI.ViewManagement;

namespace ClassNex.Styles;

internal static class ClassroomNotificationColors
{
    internal static void Attach(ProgressBar bar, Border? mask, Border? shadow)
    {
        if (Application.Current is not { PlatformSettings: { } platform } app) return;
        var theme = app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        var system = new UISettings();
        var active = false;
        var pending = 0;

        void Refresh()
        {
            var high = platform.GetColorValues().ContrastPreference == ColorContrastPreference.High;
            IBrush? accent, title = null, track = null, outline = null;
            if (high)
            {
                track = Read(UIElementType.Window, Colors.Black);
                title = outline = Read(UIElementType.WindowText, Colors.White);
                accent = Read(UIElementType.Highlight, Color.FromRgb(0x1a, 0xeb, 0xff));
                bar.Resources["ProgressBarBorderThemeThickness"] = new Thickness(1);
            }
            else
            {
                bar.Resources.Remove("ProgressBarBorderThemeThickness");
                var dark = bar.ActualThemeVariant == ThemeVariant.Dark;
                if (theme?.CustomAccentColor is { } chosen)
                {
                    Color2 color = chosen;
                    accent = new SolidColorBrush((Color)color.LightenPercent(dark ? 0.30f : -0.15f));
                }
                else if (theme?.PreferUserAccentColor == true)
                {
                    try { accent = Brush(system.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1)); }
                    catch (COMException) { accent = null; }
                }
                else accent = null;
            }

            Set(bar, "ProgressBarForeground", accent);
            Set(bar, "ProgressBarBackground", track);
            Set(bar, "ProgressBarBorderBrush", outline);
            Set(mask, "AccentFillColorDefaultBrush", high ? track : accent);
            Set(shadow, "AccentFillColorDefaultBrush", high ? track : accent);
            Set(mask, "TextOnAccentFillColorPrimaryBrush", title);
        }

        IBrush Read(UIElementType element, Color fallback)
        {
            try { return Brush(system.UIElementColor(element)); }
            catch (COMException) { return new SolidColorBrush(fallback); }
        }

        void QueueRefresh()
        {
            if (Interlocked.Exchange(ref pending, 1) != 0) return;
            Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref pending, 0); if (active) Refresh(); }, DispatcherPriority.Render);
        }
        void PlatformChanged(object? sender, PlatformColorValues e) => QueueRefresh();
        void ResourcesChanged(object? sender, ResourcesChangedEventArgs e) => QueueRefresh();
        bar.ActualThemeVariantChanged += (_, _) => QueueRefresh();
        bar.AttachedToVisualTree += (_, _) => { active = true; platform.ColorValuesChanged += PlatformChanged; app.ResourcesChanged += ResourcesChanged; Refresh(); };
        bar.DetachedFromVisualTree += (_, _) => { active = false; platform.ColorValuesChanged -= PlatformChanged; app.ResourcesChanged -= ResourcesChanged; };
    }

    private static IBrush Brush(Windows.UI.Color color) => new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

    private static void Set(Control? scope, string key, IBrush? brush)
    {
        if (scope is null) return;
        if (brush is null) { scope.Resources.Remove(key); return; }
        if (scope.Resources.TryGetValue(key, out var previous) && previous is ISolidColorBrush old && brush is ISolidColorBrush next && old.Color == next.Color) return;
        scope.Resources[key] = brush;
    }
}
