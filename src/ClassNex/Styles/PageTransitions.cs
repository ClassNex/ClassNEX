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

    public static void PlayEntrance(Control target)
    {
        if (target is null)
            return;

        target.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = Visual.OpacityProperty,
                Duration = Duration,
                Easing = Easing,
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = Duration,
                Easing = Easing,
            },
        };

        if (target.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            target.RenderTransform = shift;
        }

        target.Opacity = 0;
        shift.Y = FromVerticalOffset;

        Dispatcher.UIThread.Post(() =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                target.Opacity = 1;
                shift.Y = 0;
            }, DispatcherPriority.Render);
        }, DispatcherPriority.Background);
    }
}
