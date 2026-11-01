using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ClassNex.Styles;

public static class PageTransitions
{
    private const double FromVerticalOffset = 24;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(667);
    private static readonly SplineEasing Easing = new(new KeySpline(0.1, 0.9, 0.2, 1.0));
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, System.Threading.CancellationTokenSource> Running = new();

    public static void PlayEntrance(Control target)
    {
        if (target is null)
            return;

        if (target.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            target.RenderTransform = shift;
        }

        if (TopLevel.GetTopLevel(target) is not { IsVisible: true })
        {
            shift.Y = 0;
            target.Opacity = 1;
            return;
        }

        if (Running.TryGetValue(target, out var previous))
        {
            previous.Cancel();
            Running.Remove(target);
        }

        var animation = new Animation
        {
            Duration = Duration,
            Easing = Easing,
        };

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(0),
            Setters =
            {
                new Setter(Visual.OpacityProperty, 0.0),
                new Setter(TranslateTransform.YProperty, FromVerticalOffset),
            },
        });

        animation.Children.Add(new KeyFrame
        {
            Cue = new Cue(1),
            Setters =
            {
                new Setter(Visual.OpacityProperty, 1.0),
                new Setter(TranslateTransform.YProperty, 0.0),
            },
        });

        var token = new System.Threading.CancellationTokenSource();
        Running.Add(target, token);
        var run = animation.RunAsync(target, token.Token);
        run.ContinueWith(
            (_, _) =>
            {
                if (Running.TryGetValue(target, out var self) && ReferenceEquals(self, token))
                    Running.Remove(target);
            },
            Dispatcher.UIThread);
    }
}
