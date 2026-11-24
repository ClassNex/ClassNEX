using System;
using System.Numerics;
using Avalonia;
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
    public static void FadeIn(Control page)
    {
        if (ElementComposition.GetElementVisual(page) is not { } visual) return;
        visual.StopAnimation(nameof(visual.Opacity));
        visual.Opacity = 1;
        if (!DesktopMotion.IsEnabled) return;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = DesktopMotion.Enter;
        fade.InsertKeyFrame(0, .72f);
        fade.InsertKeyFrame(1, 1, DesktopMotion.EnterEase);
        visual.StartAnimation(nameof(visual.Opacity), fade);
    }

    public static void Play(Control page, WinUIPageTransitionKind kind)
    {
        var visual = ElementComposition.GetElementVisual(page);
        if (visual is null) return;

        visual.StopAnimation(nameof(visual.Offset));
        visual.StopAnimation(nameof(visual.Opacity));
        visual.StopAnimation(nameof(visual.Scale));

        var basePos = new Vector3((float)page.Bounds.X, (float)page.Bounds.Y, 0);
        visual.Offset = new Vector3D(basePos.X, basePos.Y, 0);
        visual.Opacity = 1;
        visual.Scale = new Vector3D(1, 1, 1);
        if (kind == WinUIPageTransitionKind.Suppress || !DesktopMotion.IsEnabled) return;

        var compositor = visual.Compositor;
        if (kind == WinUIPageTransitionKind.DrillIn)
        {
            visual.CenterPoint = new Vector3D(
                (float)(page.Bounds.Width / 2),
                (float)(page.Bounds.Height / 2),
                0);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.Duration = DesktopMotion.Enter;
            scale.InsertKeyFrame(0, new Vector3(.985f, .985f, 1));
            scale.InsertKeyFrame(1, Vector3.One, DesktopMotion.EnterEase);
            visual.StartAnimation(nameof(visual.Scale), scale);
        }
        else
        {
            var distance = kind switch
            {
                WinUIPageTransitionKind.SlideFromLeft => new Vector3(-12, 0, 0),
                WinUIPageTransitionKind.SlideFromRight => new Vector3(12, 0, 0),
                _ => new Vector3(0, 6, 0)
            };
            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.Duration = DesktopMotion.Enter;
            offset.InsertKeyFrame(0, basePos + distance);
            offset.InsertKeyFrame(1, basePos, DesktopMotion.EnterEase);
            visual.StartAnimation(nameof(visual.Offset), offset);
        }
        FadeIn(page);
    }
}
