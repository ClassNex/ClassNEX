#if DEBUG
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewDesktopPointerAsync(MainWindow window, Action<bool, string> check)
    {
        var settings = AppServices.Settings;
        var saved = (settings.IsClickThrough, settings.HoverOpacity, settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock);
        var hover = window.FindControl<Panel>("HoverHost")!;
        var title = window.FindControl<ScrollViewer>("MaskTitleViewport")!;
        var work = window.Screens.Primary!.WorkingArea;
        PixelRect Island() => window.GetIslandScreenRect() ?? throw new InvalidOperationException("Classroom has no on-screen bounds.");
        PixelPoint Center() { var r = Island(); return new(r.X + r.Width / 2, r.Y + r.Height / 2); }
        static async Task Wait(Func<bool> ready, string failure)
        {
            var time = Stopwatch.StartNew();
            while (!ready())
            {
                if (time.Elapsed.TotalSeconds > 4) throw new InvalidOperationException(failure);
                await Task.Delay(16);
            }
        }
        Task HoverAt(PixelPoint p, double opacity) => Wait(() =>
        {
            window.UpdateHoverFadeAt(p);
            return Math.Abs(hover.Opacity - opacity) <= .001;
        }, $"Classroom did not reach opacity {opacity} at {p}.");

        check(!window.IsEditMode && !settings.IsClickThrough && settings.HoverOpacity < 1,
            "The anonymous classroom starts in normal mode with hover fading enabled, permanent pass-through disabled");
        try
        {
            await Wait(() => window.IsVisible && window.GetIslandScreenRect() is { Width: > 8, Height: > 8 }, "Classroom did not become visible.");
            settings.HoverOpacity = 1;
            window.ApplySettings();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            var handle = window.TryGetPlatformHandle()!.Handle;
            using var target = new DesktopPointerTarget(handle, work);
            target.Calibrate(Center());
            void Click(bool through, string description, PixelPoint? point = null)
            {
                target.Click(point ?? Center(), through);
                check(true, description);
            }
            check(true, target.UsesWindowFromPoint
                ? "Non-input review desktop: HWND Z-order and overlap, direct WindowFromPoint, child hit tests and native button state are checked for every click"
                : "Non-input review desktop: WindowFromPoint could not resolve the opaque classroom; HWND Z-order and overlap, coordinate-based ChildWindowFromPointEx and native button state are checked for every click");
            await HoverAt(Center(), 1);
            Click(false, "At 100% hover opacity the interactive classroom intercepts the click; the native button behind it stays unchanged");

            var before = Island().Position;
            var origin = Center();
            var end = new PixelPoint(origin.X + 24, origin.Y + 40);
            var hit = window.InputHitTest(window.PointToClient(origin)) as InputElement ?? throw new InvalidOperationException("No classroom drag target.");
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, window, window.PointToClient(origin), 1,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            var receiver = pointer.Captured as InputElement ?? hit;
            try
            {
                receiver.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, receiver, pointer, window, window.PointToClient(end), 2,
                    new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
                check(Island().Position == before, "Dragging the classroom surface does not move the real window in normal mode");
            }
            finally
            {
                receiver.RaiseEvent(new PointerReleasedEventArgs(receiver, pointer, window, window.PointToClient(end), 3,
                    new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            }
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            check(Island().Position == before && (settings.MainWindowLeft, settings.MainWindowTop) == (saved.MainWindowLeft, saved.MainWindowTop),
                "Releasing an ordinary classroom drag leaves the saved anchor untouched");

            settings.HoverOpacity = saved.HoverOpacity;
            window.ApplySettings();
            // Do not yield or sample hover between enabling fading and delivering this first click.
            Click(true, "First click after enabling the default hover fade changes the button behind the classroom before any hover sample");
            await HoverAt(Center(), saved.HoverOpacity);
            Click(true, "The native button behind the visibly faded classroom receives the click");
            await HoverAt(new PixelPoint(Island().X - 2, Island().Y - 2), 1);
            check(!settings.IsClickThrough, "Leaving the island restores full visual opacity without changing the permanent pass-through preference");

            settings.HoverOpacity = 1;
            settings.IsClickThrough = true;
            window.ApplySettings();
            Click(true, "Permanent pass-through allows the native button to be clicked even at 100% hover opacity");
            settings.HoverOpacity = saved.HoverOpacity;
            window.ApplySettings();
            await HoverAt(Center(), saved.HoverOpacity);
            window.EnterEditMode();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            await HoverAt(Center(), 1);
            var grip = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "MoveIslandHandle" && c.IsEffectivelyVisible);
            PixelPoint GripCenter() => grip.PointToScreen(new Point(grip.Bounds.Width / 2, grip.Bounds.Height / 2));
            string? hitName = null;
            try
            {
                await Wait(() =>
                {
                    var actual = window.InputHitTest(window.PointToClient(GripCenter())) as Visual;
                    hitName = actual?.GetType().Name + "#" + (actual as Control)?.Name;
                    return actual != null && actual.GetSelfAndVisualAncestors().Contains(grip);
                }, "The editor handle did not become hit-testable.");
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException($"The editing handle at {GripCenter()} missed; hit={hitName}, local={window.PointToClient(GripCenter())}, window bounds={window.Bounds}, client={window.ClientSize}, pos={window.Position}, island={Island()}.", error);
            }
            Click(false, "In edit mode the real editing handle intercepts the click even though both pass-through preferences are still enabled", GripCenter());
            var editing = Island();
            var goal = MainWindow.ClampIsland(work, editing.Size, new PixelPoint(editing.X, editing.Y + (editing.Y < work.Y + work.Height / 2 ? 72 : -72)));
            check(goal != editing.Position, "The editor drag target is distinguishable from the starting window position");
            await MoveClassroomInEditorAsync(window, goal, live => check(live.Position == goal,
                "Moving the real editing handle relocates the window while its pointer is held"));
            var stored = SettingsService.Load();
            check(!window.IsEditMode && Island().Position == goal && stored.MainWindowLeft == goal.X && stored.MainWindowTop == goal.Y &&
                settings.IsClickThrough && stored.IsClickThrough && stored.HoverOpacity == saved.HoverOpacity,
                "Releasing the editing handle saves its new location; leaving editing preserves permanent pass-through");
            Click(true, "After leaving editing, the native button behind the relocated classroom can be clicked again");

            window.ShowNotificationMask(string.Join('\n', Enumerable.Repeat("请收好课本和作业。", 100)));
            await Wait(() => title.Viewport.Height > 0 && title.Extent.Height > title.Viewport.Height + 1 && hover.Opacity >= .999,
                "An overflowing reminder did not become an unfaded reading surface.");
            Click(false, "An overflowing reminder intercepts input for scrolling instead of clicking the native button underneath");
            window.ShowNotificationMask("准备上课");
            await Wait(() => title.Extent.Height <= title.Viewport.Height + 1, "Short reminder did not release its overflow.");
            Click(true, "Replacing the overflowing reminder with a short one restores the button behind the notice");
            window.HideNotificationMask();
            Click(true, "Dismissing the reminder also keeps the desktop clickable");
            check(settings.IsClickThrough && settings.HoverOpacity == saved.HoverOpacity,
                "Editing, reading and dismissing never overwrite either saved pointer preference");
        }
        finally
        {
            window.ExitEditMode();
            window.HideNotificationMask();
            (settings.IsClickThrough, settings.HoverOpacity, settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock) = saved;
            AppServices.SaveSettings();
        }
    }
}
#endif
