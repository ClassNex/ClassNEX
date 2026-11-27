#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewReminderWheelAsync(MainWindow window, Action<bool, string> check, bool interactive = false)
    {
        var settings = AppServices.Settings;
        var previousClickThrough = settings.IsClickThrough;
        var previousHover = settings.HoverOpacity;
        var previousTitle = window.Title;
        var previouslyListed = window.ShowInTaskbar;
        var handle = window.TryGetPlatformHandle()!.Handle;
        var mask = window.FindControl<Border>("NotificationMask")!;
        var title = window.FindControl<TextBlock>("NotificationMaskText")!;
        var titleView = window.FindControl<ScrollViewer>("MaskTitleViewport")!;
        var body = window.FindControl<ScrollViewer>("NotificationBodyViewport")!;
        var plain = window.FindControl<TextBlock>("NotificationSimpleMessage")!;
        var course = window.FindControl<TextBlock>("NotificationOverlaySubject")!;
        var slides = window.FindControl<ListBox>("NotificationSlides")!;
        var hover = window.FindControl<Panel>("HoverHost")!;
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        bool PassesThrough() => (GetWindowLongW(handle, -20) & 0x20) != 0;
        var longText = string.Join('\n', Enumerable.Repeat("请带好书包和作业。", 100)) + "最后一项";

        static async Task Wait(Func<bool> condition, string reason, int seconds = 4)
        {
            var start = Stopwatch.GetTimestamp();
            while (!condition())
            {
                if (Stopwatch.GetElapsedTime(start).TotalSeconds > seconds) throw new InvalidOperationException(reason);
                await Task.Delay(20);
            }
        }

        async Task WheelToEnd(ScrollViewer view, TextBlock label, string description)
        {
            await Wait(() => !PassesThrough() && view.Viewport.Height > 1 && view.Extent.Height > view.Viewport.Height + 20 && hover.Opacity >= .999,
                description + ": no scrollable, non-faded input surface");
            var before = view.Offset.Y;
            int events = 0;
            string? hit = null, source = null;
            void WheelReceived(object? _, PointerWheelEventArgs e) { events++; source = (e.Source as Control)?.Name ?? e.Source?.GetType().Name; }
            window.AddHandler(InputElement.PointerWheelChangedEvent, WheelReceived, RoutingStrategies.Tunnel, true);
            try
            {
                if (interactive)
                {
                    const int inputFlags = 0x08000000 | 0x20;
                    var noticeFlags = GetWindowLongW(handle, -20) & inputFlags;
                    try
                    {
                        WindowsOverlay.SetToolWindow(handle, false);
                        check(noticeFlags == 0x08000000 && (GetWindowLongW(handle, -20) & inputFlags) == noticeFlags,
                            "The interactive test window is discoverable without changing the notice's native input and no-activation flags");
                        await Wait(() => events > 0 && view.Offset.Y + view.Viewport.Height >= view.Extent.Height - 1,
                            "No native wheel reached the last line during the interactive check", 120);
                    }
                    finally
                    {
                        WindowsOverlay.SetToolWindow(handle, true);
                    }
                }
                else
                {
                    for (int i = 0; i < 64 && view.Offset.Y + view.Viewport.Height < view.Extent.Height - 1; i++)
                    {
                        var point = view.PointToScreen(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2));
                        var position = window.PointToClient(point);
                        var input = window.InputHitTest(position) as InputElement ?? throw new InvalidOperationException("No wheel hit target");
                        hit = (input as Control)?.Name ?? input.GetType().Name;
                        if (IsMouseInPointerEnabled())
                            input.RaiseEvent(new PointerWheelEventArgs(input, pointer, window, position, (ulong)Environment.TickCount64,
                                new(RawInputModifiers.None, PointerUpdateKind.Other), KeyModifiers.None, new Vector(0, -3)));
                        else
                            SendMessageW(handle, 0x020A, (-120 * 3) << 16, (point.X & 0xffff) | (point.Y << 16));
                        await Task.Delay(16);
                    }
                }
            }
            finally { window.RemoveHandler(InputElement.PointerWheelChangedEvent, WheelReceived); }
            Point? Tail() => label.TranslatePoint(new Point(0, label.TextLayout.Height), view);
            await Wait(() => Tail() is { } end && end.Y >= 0 && end.Y <= view.Bounds.Height + 1,
                description + ": the last line never entered the actual viewport after scrolling");
            var tail = Tail();
            check(events > 0 && view.Offset.Y > before && view.Offset.Y + view.Viewport.Height >= view.Extent.Height - 1 &&
                label.TextLayout.TextLines.LastOrDefault() is { HasCollapsed: false } last && last.FirstTextSourceIndex + last.Length >= label.Text!.Length &&
                tail.HasValue && tail.Value.Y >= 0 && tail.Value.Y <= view.Bounds.Height + 1 && settings.IsClickThrough && settings.HoverOpacity == .05,
                $"{description}: the {(interactive ? "desktop-delivered" : "fixture-dispatched")} wheel reaches the last visible line with saved click-through and hover preferences intact (offset {view.Offset.Y:0.#}, extent {view.Extent.Height:0.#}, viewport {view.Viewport.Height:0.#}, text end at {tail?.Y:0.#}, events {events}, source {source ?? hit})");
        }

        try
        {
            if (interactive)
            {
                window.Title = "ClassNEX · 匿名滚轮验收";
                window.ShowInTaskbar = true;
            }
            settings.IsClickThrough = true;
            settings.HoverOpacity = .05;
            window.ApplySettings();
            window.ShowNotificationMask("准备上课");
            check(PassesThrough(), "A normal title keeps the default Windows click-through policy");
            if (!interactive)
            {
                window.ShowNotificationMask(longText);
                await WheelToEnd(titleView, title, "Oversize title");
                CheckIslandRegion(window, check);
            }
            window.ShowNotificationMask("放学");
            if (!interactive) check(PassesThrough(), "Replacing a scrollable title with a short title immediately gives input back to the desktop");
            window.ShowNotificationOverlay("", "", "", "", message: interactive ? string.Join('\n', Enumerable.Range(1, 60).Select(n => $"{n:00}   放学事项：请收好课本和作业")) + "（最后一项）" : longText);
            await Wait(() => !mask.IsVisible, "The dismissal title did not uncover the long message");
            await WheelToEnd(body, plain, "Oversize dismissal");
            window.HideNotificationMask();
            check(PassesThrough(), "Exit releases the scrollable notice's input region before the fade completes");
            if (interactive) return;

            window.ShowNotificationMask("课间");
            window.ShowNotificationOverlay("下节课", longText, "", "10:30–11:10", message: "请准备。", verticalPageTurn: true);
            await Wait(() => !mask.IsVisible && slides.SelectedIndex == 0, "The tall course page did not appear");
            await WheelToEnd(body, course, "Oversize course slide");
            await Wait(() => slides.SelectedIndex == 1 && PassesThrough() && body.Offset.Y == 0,
                "Switching to a short slide left the preceding page's input region or offset", 13);
            check(body.Bounds.Height < 80, "On the short slide the same notification shrinks, resets scrolling and leaves the desktop clickable");
            await Wait(() => slides.SelectedIndex == 0 && !PassesThrough(), "Returning to the oversize slide did not restore scrolling", 13);
            window.ShowNexPanel("课堂问答", false);
            check(PassesThrough(), "NEX interrupting a scrollable notice immediately restores the saved native input policy");
            window.HideNexPanel();
            check(PassesThrough(), "The restored classroom keeps its original click-through policy");
        }
        finally
        {
            window.HideNexPanel();
            window.HideNotificationMask();
            if (interactive)
            {
                window.Title = previousTitle;
                window.ShowInTaskbar = previouslyListed;
            }
            settings.IsClickThrough = previousClickThrough;
            settings.HoverOpacity = previousHover;
            window.ApplySettings();
        }
    }

    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool IsMouseInPointerEnabled();
}
#endif
