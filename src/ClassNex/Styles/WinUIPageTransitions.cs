using System;
using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;

namespace ClassNex.Styles;

public enum WinUIPageTransitionKind
{
    Entrance,
    SlideFromLeft,
    SlideFromRight,
    DrillIn,
    Suppress,
}

public static class WinUIPageTransitions
{
    private const float EntranceOffsetY = 24;
    private const float SlideOffsetX = 200;

    public static void Play(Control page, WinUIPageTransitionKind kind)
    {
        var visual = ElementComposition.GetElementVisual(page);
        if (visual is null || kind == WinUIPageTransitionKind.Suppress)
            return;

        if (page.Bounds.Width <= 0)
            page.UpdateLayout();

        visual.StopAnimation(nameof(visual.Offset));
        visual.StopAnimation(nameof(visual.Opacity));
        visual.StopAnimation(nameof(visual.Scale));

        var basePos = new Vector3((float)page.Bounds.X, (float)page.Bounds.Y, 0);
        visual.Offset = new Vector3D(basePos.X, basePos.Y, 0);
        visual.Opacity = 1;
        visual.Scale = new Vector3D(1, 1, 1);

        var compositor = visual.Compositor;
        var duration = kind switch
        {
            WinUIPageTransitionKind.SlideFromLeft or WinUIPageTransitionKind.SlideFromRight =>
                TimeSpan.FromMilliseconds(300),
            WinUIPageTransitionKind.DrillIn => TimeSpan.FromMilliseconds(783),
            _ => TimeSpan.FromMilliseconds(333),
        };

        if (kind is WinUIPageTransitionKind.SlideFromLeft or WinUIPageTransitionKind.SlideFromRight)
        {
            var direction = kind == WinUIPageTransitionKind.SlideFromRight ? 1 : -1;
            var startPos = new Vector3(basePos.X + SlideOffsetX * direction, basePos.Y, 0);
            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.Duration = duration;
            offset.InsertKeyFrame(0, startPos);
            offset.InsertKeyFrame(0.5f, startPos, new LinearEasing());
            offset.InsertKeyFrame(1, basePos, Easing.Parse("0.13, 0.96, 0.22, 1"));
            visual.StartAnimation(nameof(visual.Offset), offset);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.Duration = duration;
            opacity.InsertKeyFrame(0, 0);
            opacity.InsertKeyFrame(0.5f, 0, new LinearEasing());
            opacity.InsertKeyFrame(0.505f, 1, new LinearEasing());
            opacity.InsertKeyFrame(1, 1, new LinearEasing());
            visual.StartAnimation(nameof(visual.Opacity), opacity);
            return;
        }

        if (kind == WinUIPageTransitionKind.DrillIn)
        {
            visual.CenterPoint = new Vector3D(
                (float)(page.Bounds.Width / 2),
                (float)(page.Bounds.Height / 2),
                0);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.Duration = duration;
            scale.InsertKeyFrame(0, new Vector3(0.94f, 0.94f, 1));
            scale.InsertKeyFrame(1, Vector3.One, Easing.Parse("0.12, 0, 0, 1"));
            visual.StartAnimation(nameof(visual.Scale), scale);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.Duration = TimeSpan.FromMilliseconds(333);
            opacity.InsertKeyFrame(0, 0);
            opacity.InsertKeyFrame(1, 1, Easing.Parse("0.25, 1, 0.5, 1"));
            visual.StartAnimation(nameof(visual.Opacity), opacity);
            return;
        }

        var entranceOffset = compositor.CreateVector3KeyFrameAnimation();
        entranceOffset.Duration = duration;
        entranceOffset.InsertKeyFrame(0, new Vector3(basePos.X, basePos.Y + EntranceOffsetY, 0));
        entranceOffset.InsertKeyFrame(1, basePos, Easing.Parse("0.1, 0.9, 0.2, 1"));
        visual.StartAnimation(nameof(visual.Offset), entranceOffset);

        var entranceOpacity = compositor.CreateScalarKeyFrameAnimation();
        entranceOpacity.Duration = duration;
        entranceOpacity.InsertKeyFrame(0, 0);
        entranceOpacity.InsertKeyFrame(1, 1, Easing.Parse("0.1, 0.9, 0.2, 1"));
        visual.StartAnimation(nameof(visual.Opacity), entranceOpacity);
    }
}
