using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace ClassNex.Styles;

public static class PageTransitions
{
    private static readonly SplineEasing EntranceEasing =
        new(new KeySpline(0.1, 0.9, 0.2, 1.0));

    public static void PlayEntrance(Control target)
    {
        if (target is null)
            return;

        const double fromOffset = 24;

        var shift = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(250),
            Easing = EntranceEasing,
        };
        shift.Children.Add(new KeyFrame
        {
            Cue = new Cue(0),
            Setters = { new Setter(TranslateTransform.YProperty, fromOffset) },
        });
        shift.Children.Add(new KeyFrame
        {
            Cue = new Cue(1),
            Setters = { new Setter(TranslateTransform.YProperty, 0.0) },
        });

        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new LinearEasing(),
        };
        fade.Children.Add(new KeyFrame
        {
            Cue = new Cue(0),
            Setters = { new Setter(Visual.OpacityProperty, 0.0) },
        });
        fade.Children.Add(new KeyFrame
        {
            Cue = new Cue(1),
            Setters = { new Setter(Visual.OpacityProperty, 1.0) },
        });

        target.RenderTransform ??= new TranslateTransform();

        _ = shift.RunAsync(target);
        _ = fade.RunAsync(target);
    }
}
