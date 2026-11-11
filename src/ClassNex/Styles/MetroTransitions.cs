using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ClassNex.Styles;

public static class MetroTransitions
{
    private const double FlipMs = 360;
    private const double StepDelayMs = 28;
    private const float HingeShiftX = 18;
    private const int MaxAnimatedItems = 12;

    private static readonly HashSet<Control> TouchedControls = new();
    private static Control? _activePage;

    public static void Play(
        Control? from,
        Control to,
        bool forward,
        Action? completed = null,
        Control? header = null)
    {
        _activePage = to;

        if (from is not null && !ReferenceEquals(from, to))
        {
            from.IsHitTestVisible = false;
            from.Opacity = 0;
        }

        var needsLayout = to.Bounds.Width <= 0 || header?.Bounds.Height <= 0;
        if (!to.IsVisible)
            to.IsVisible = true;

        if (needsLayout)
            to.UpdateLayout();

        to.Opacity = 1;
        to.IsHitTestVisible = true;
        RenderOptions.SetTextRenderingMode(to, TextRenderingMode.Antialias);

        var scroll = to.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        var viewportHeight = scroll is { Viewport.Height: > 100 }
            ? scroll.Viewport.Height
            : to.Bounds.Height > 100 ? to.Bounds.Height : 680.0;

        var sequence = new List<Control>();
        if (header is not null)
            sequence.Add(header);

        foreach (var child in CollectChildren(to).Where(c => c.IsVisible))
        {
            var top = scroll is not null
                ? child.TranslatePoint(new Point(0, 0), scroll)?.Y ?? double.MaxValue
                : child.Bounds.Y;
            var bottom = top + child.Bounds.Height;

            if (top < viewportHeight + 24 && bottom > -24)
                sequence.Add(child);
            else if (TouchedControls.Contains(child))
                ResetVisualToStatic(child);
        }

        if (!forward)
            sequence.Reverse();

        StartHardwareAnimations(sequence);
        completed?.Invoke();
    }

    public static void PrewarmPagesAtIdle(IEnumerable<Control> pages, Control currentActivePage)
    {
        foreach (var page in pages)
        {
            if (ReferenceEquals(page, currentActivePage))
                continue;

            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(page, currentActivePage) ||
                    ReferenceEquals(page, _activePage))
                    return;

                if (!page.IsVisible)
                    page.IsVisible = true;
                if (page.Bounds.Width <= 0)
                    page.UpdateLayout();

                if (!ReferenceEquals(page, _activePage) &&
                    !ReferenceEquals(page, currentActivePage))
                    page.Opacity = 0;
            }, DispatcherPriority.Background);
        }
    }

    public static void PlayList(Panel panel, int direction = 1)
    {
        panel.UpdateLayout();

        var sequence = panel.Children
            .OfType<Control>()
            .Where(c => c.IsVisible)
            .Take(MaxAnimatedItems)
            .ToList();

        StartHardwareAnimations(sequence);
    }

    public static void PlayTrayMenuTransition(Panel menuItemsContainer)
    {
        if (menuItemsContainer.Bounds.Width <= 0)
            menuItemsContainer.UpdateLayout();

        var sequence = new List<Control>(menuItemsContainer.Children.Count);
        foreach (var child in menuItemsContainer.Children)
        {
            if (child is Control { IsVisible: true } c)
                sequence.Add(c);
        }

        if (sequence.Count == 0)
            return;

        sequence.Reverse();

        var first = ElementComposition.GetElementVisual(sequence[0]);
        if (first is null)
            return;

        var compositor = first.Compositor;
        var smoothOut = Easing.Parse("0.18,1,0.30,1");
        var linear = new LinearEasing();

        const double menuFlipMs = 280;
        const double menuStepMs = 16;
        const float menuHingeShift = 16;

        for (var i = 0; i < sequence.Count; i++)
        {
            var control = sequence[i];
            RenderOptions.SetTextRenderingMode(control, TextRenderingMode.Antialias);
            var visual = ElementComposition.GetElementVisual(control);
            if (visual is null)
                continue;

            var basePos = new System.Numerics.Vector3(
                (float)control.Bounds.X,
                (float)control.Bounds.Y,
                0f);
            var startPos = new System.Numerics.Vector3(
                basePos.X - menuHingeShift,
                basePos.Y,
                0f);
            var height = (float)Math.Max(control.Bounds.Height, 24.0);

            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Offset");
            visual.CenterPoint = new Vector3D(-24f, height * 0.5f, 0f);
            visual.Scale = new Vector3D(1, 1, 1);
            visual.Offset = new Vector3D(basePos.X, basePos.Y, 0);
            visual.Opacity = 1f;

            var startScale = new System.Numerics.Vector3(0.25f, 0.92f, 1f);
            var delayMs = i * menuStepMs;
            var totalMs = delayMs + menuFlipMs;
            var startCue = (float)(delayMs / totalMs);
            var fastOpacityCue = startCue + (1f - startCue) * 0.30f;
            var duration = TimeSpan.FromMilliseconds(totalMs);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.Duration = duration;
            opacity.InsertKeyFrame(0f, 0f);
            if (startCue > 0.001f)
                opacity.InsertKeyFrame(startCue, 0f, linear);
            opacity.InsertKeyFrame(fastOpacityCue, 0.92f, smoothOut);
            opacity.InsertKeyFrame(1f, 1f, smoothOut);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.Duration = duration;
            scale.InsertKeyFrame(0f, startScale);
            if (startCue > 0.001f)
                scale.InsertKeyFrame(startCue, startScale, linear);
            scale.InsertKeyFrame(1f, System.Numerics.Vector3.One, smoothOut);

            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.Duration = duration;
            offset.InsertKeyFrame(0f, startPos);
            if (startCue > 0.001f)
                offset.InsertKeyFrame(startCue, startPos, linear);
            offset.InsertKeyFrame(1f, basePos, smoothOut);

            visual.StartAnimation("Opacity", opacity);
            visual.StartAnimation("Scale", scale);
            visual.StartAnimation("Offset", offset);
        }
    }

    private static void StartHardwareAnimations(IReadOnlyList<Control> items)
    {
        if (items.Count == 0)
            return;

        var first = ElementComposition.GetElementVisual(items[0]);
        if (first is null)
            return;

        var compositor = first.Compositor;
        var crisp = Easing.Parse("0.08,0.82,0.18,1");
        var linear = new LinearEasing();

        for (var i = 0; i < items.Count; i++)
        {
            var control = items[i];
            RenderOptions.SetTextRenderingMode(control, TextRenderingMode.Antialias);
            var visual = ElementComposition.GetElementVisual(control);
            if (visual is null)
                continue;

            TouchedControls.Add(control);

            var basePos = new System.Numerics.Vector3(
                (float)control.Bounds.X,
                (float)control.Bounds.Y,
                0f);
            var startPos = new System.Numerics.Vector3(
                basePos.X - HingeShiftX,
                basePos.Y,
                0f);
            var height = (float)Math.Max(control.Bounds.Height, 36.0);

            visual.StopAnimation("Opacity");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Offset");
            visual.CenterPoint = new Vector3D(-32f, height * 0.5f, 0f);
            visual.Scale = new Vector3D(1, 1, 1);
            visual.Offset = new Vector3D(basePos.X, basePos.Y, 0);
            visual.Opacity = 1f;

            var startScale = new System.Numerics.Vector3(0.58f, 1f, 1f);
            var delayMs = i * StepDelayMs;
            var totalMs = delayMs + FlipMs;
            var startCue = (float)(delayMs / totalMs);
            var opacityDoneCue = Math.Min(1f, (float)((delayMs + 65) / totalMs));
            var duration = TimeSpan.FromMilliseconds(totalMs);

            var opacity = compositor.CreateScalarKeyFrameAnimation();
            opacity.Duration = duration;
            opacity.InsertKeyFrame(0f, 0f);
            if (startCue > 0.001f)
                opacity.InsertKeyFrame(startCue, 0f, linear);
            if (opacityDoneCue < 0.999f)
                opacity.InsertKeyFrame(opacityDoneCue, 1f, linear);
            opacity.InsertKeyFrame(1f, 1f, linear);

            var scale = compositor.CreateVector3KeyFrameAnimation();
            scale.Duration = duration;
            scale.InsertKeyFrame(0f, startScale);
            if (startCue > 0.001f)
                scale.InsertKeyFrame(startCue, startScale, linear);
            scale.InsertKeyFrame(1f, System.Numerics.Vector3.One, crisp);

            var offset = compositor.CreateVector3KeyFrameAnimation();
            offset.Duration = duration;
            offset.InsertKeyFrame(0f, startPos);
            if (startCue > 0.001f)
                offset.InsertKeyFrame(startCue, startPos, linear);
            offset.InsertKeyFrame(1f, basePos, crisp);

            visual.StartAnimation("Opacity", opacity);
            visual.StartAnimation("Scale", scale);
            visual.StartAnimation("Offset", offset);
        }
    }

    private static void ResetVisualToStatic(Control control)
    {
        var visual = ElementComposition.GetElementVisual(control);
        if (visual is null)
            return;

        var basePos = new System.Numerics.Vector3(
            (float)control.Bounds.X,
            (float)control.Bounds.Y,
            0f);

        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.StopAnimation("Offset");
        visual.Scale = new Vector3D(1, 1, 1);
        visual.Offset = new Vector3D(basePos.X, basePos.Y, 0);
        visual.Opacity = 1f;
    }

    private static IEnumerable<Control> CollectChildren(Control page)
    {
        if (page is Panel panel)
            return panel.Children.OfType<Control>();

        if (page is ContentControl { Content: Control content })
            return CollectChildren(content);

        if (page is Decorator { Child: Control child })
            return CollectChildren(child);

        return Array.Empty<Control>();
    }
}
