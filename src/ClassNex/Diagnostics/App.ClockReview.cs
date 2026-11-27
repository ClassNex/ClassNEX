#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewClockLayoutAsync(MainWindow window, Func<Window, string, string, int, Task> shot, Action<bool, string> check)
    {
        var fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(fixture, ".classnex-review")) || AccountService.Current != null || AppServices.Settings.CentralManagement.IsEnabled)
            throw new InvalidOperationException("Clock layout review requires an anonymous portable fixture.");
        var widgets = AppServices.Widgets;
        var original = AppServices.Settings.Widgets.ToList();
        var initialLeft = AppServices.Settings.MainWindowLeft;
        var initialTop = AppServices.Settings.MainWindowTop;
        var initialPassthrough = AppServices.Settings.IsClickThrough;
        var initialHover = AppServices.Settings.HoverOpacity;
        var mode = window.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        var dpi = (int)Math.Round(96 * window.RenderScaling);
        var moment = new DateTime(2026, 11, 22, 23, 59, 0);
        var host = window.FindControl<StackPanel>("WidgetHost")!;
        TextBlock? Text(string name) => host.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(x => x.Name == name && x.IsEffectivelyVisible);
        async Task Inspect(string name, params string[] expected)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            window.UpdateLayout();
            check(host.Children.Count == Math.Max(1, expected.Length), name + ": visible widget slots");
            check((Text("DateValue") != null) == expected.Contains("date") && (Text("ClockValue") != null) == expected.Contains("clock"), name + ": independent date and clock");
            if (expected.Length <= 1)
                check(!host.GetVisualDescendants().OfType<Border>().Any(x => x.Name == "DateSeparator" && x.IsEffectivelyVisible), name + ": no trailing separator");
            if (expected.Length == 1)
                check(host.Children[0].Margin == default && Math.Abs(host.Bounds.Width - host.Children[0].Bounds.Width) <= 1, name + ": no phantom slot");
            await shot(window, "clock-" + name, mode, dpi);
        }
        try
        {
            await shot(window, "overlay-restored", mode, dpi);
            var upcoming = AppServices.Day.Read(AppServices.ClassroomNow).Next;
            if (upcoming != null)
                check(host.GetVisualDescendants().OfType<Border>().Any(b => b.Tag is CourseSlot slot && slot == upcoming && b.Child is TextBlock text && text.Text == slot.DisplayName),
                    "Upcoming course uses the Alpha short chip; its tooltip retains the full title and time");
            AppServices.Settings.Widgets = AppSettings.CreateDefaultWidgets();
            widgets.Remove(widgets.Widgets.Single(w => w.Type == "schedule"));
            await Inspect("both", "date", "clock");
            window.RefreshWidgetsAt(moment);
            var firstWidth = MeasureClock();
            check(Text("DateDay")?.Text == "周日" && Text("DateValue")?.Text == "11/22" && ClockContent(Text("ClockValue")) == "23:59", "Sunday before midnight has no parity or dangling punctuation");
            window.RefreshWidgetsAt(moment.AddMinutes(1));
            check(Text("DateDay")?.Text == "周一" && Text("DateValue")?.Text == "11/23" && ClockContent(Text("ClockValue")) == "00:00", "Clock and date roll independently through local midnight");
            window.RefreshWidgetsAt(moment.Date.AddHours(11).AddMinutes(11));
            check(Math.Abs(MeasureClock() - firstWidth) < .1, "11:11 and 23:59 occupy the same width");
            var clock = widgets.Widgets.Single(w => w.Type == "clock");
            clock.FlashTimeSeparator = true;
            window.RefreshWidgetsAt(moment);
            var lit = MeasureClock();
            window.RefreshWidgetsAt(moment.AddSeconds(1));
            check(Math.Abs(MeasureClock() - lit) < .1, "Flashing the time separator does not move the digits");
            clock.FlashTimeSeparator = false;
            clock.IsEnabled = false;
            AppServices.SaveSettings();
            await Inspect("date-only", "date");
            clock.IsEnabled = true;
            widgets.Remove(widgets.Widgets.Single(w => w.Type == "date"));
            await Inspect("time-only", "clock");
            window.RefreshWidgetsAt(moment.AddMinutes(3));
            check(ClockContent(Text("ClockValue")) == "00:02", "Clock continues without a date widget");
            widgets.Remove(widgets.Widgets.Single());
            await Inspect("neither");
            widgets.Add("date");
            var moved = widgets.Add("clock");
            widgets.Move(moved, -1);
            await Inspect("swapped", "clock", "date");
            window.ResetPlacement();
            AppServices.Settings.IsClickThrough = false;
            AppServices.Settings.HoverOpacity = 1;
            AppServices.SaveSettings();
            await Settle();
            var work = window.Screens.Primary!.WorkingArea;
            var initial = window.GetIslandScreenRect()!.Value;
            bool At(PixelPoint target) => window.GetIslandScreenRect() is { } rect && Math.Abs(rect.X - target.X) <= 1 && Math.Abs(rect.Y - target.Y) <= 1;
            check(At(MainWindow.ClampIsland(work, initial.Size, null)), "80/80 starts the visible island at the working area's top center");
            PixelPoint ClockCenter()
            {
                var current = Text("ClockValue") ?? throw new InvalidOperationException("No visible clock for the classroom pointer review");
                return current.PointToScreen(new Point(current.Bounds.Width / 2, current.Bounds.Height / 2));
            }
            InputElement Hit(PixelPoint point) => window.InputHitTest(window.PointToClient(point)) as InputElement ??
                throw new InvalidOperationException($"Classroom hit test missed at {point}; island={window.GetIslandScreenRect()}, window={window.Position}");
            var press = ClockCenter();
            var source = Hit(press);
            var pointer = Mouse();
            static Pointer Mouse() => new(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            ulong timestamp = 0;
            var intended = new PixelPoint(work.X + work.Width / 6, work.Y + work.Height / 5);
            var target = MainWindow.ClampIsland(work, initial.Size, intended);
            var end = new PixelPoint(press.X + target.X - initial.X, press.Y + target.Y - initial.Y);
            void Press(int clicks = 1) => source.RaiseEvent(new PointerPressedEventArgs(source, pointer, window, window.PointToClient(press), ++timestamp,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, clicks));
            void Move(PixelPoint point) => source.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, source, pointer, window, window.PointToClient(point), ++timestamp,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            void Release(PixelPoint point) => source.RaiseEvent(new PointerReleasedEventArgs(source, pointer, window, window.PointToClient(point), ++timestamp,
                new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Press(); Move(new PixelPoint(press.X + 1, press.Y + 1)); Release(press);
            check(AppServices.Settings.MainWindowLeft == 80 && AppServices.Settings.MainWindowTop == 80 && At(initial.Position), "A click with one pixel of jitter does not move the island");
            Press(); Move(end); Release(end); await Settle();
            check(AppServices.Settings.MainWindowLeft == 80 && AppServices.Settings.MainWindowTop == 80 && At(initial.Position), "An ordinary classroom drag never moves the island, even when classroom shortcuts are enabled");
            var settingsWindow = ((App)Current!)._desktop!.Settings!;
            await FromGeneral();
            await MoveClassroomInEditorAsync(window, target, island =>
                check(SettingsService.Load() is { MainWindowLeft: 80, MainWindowTop: 80 } && island.Position == target,
                    "The explicit editing handle moves the window without writing intermediate positions"));
            check(AppServices.Settings.MainWindowLeft == target.X && AppServices.Settings.MainWindowTop == target.Y && At(target), "Releasing the edit handle saves the screen-pixel anchor; leaving edit mode keeps it");
            CheckIslandRegion(window, check);
            await shot(window, "overlay-dragged", mode, dpi);
            async Task<bool> Reached(string key)
            {
                var elapsed = Stopwatch.StartNew();
                while (settingsWindow.CurrentPageKey != key && elapsed.ElapsedMilliseconds < 1500) await Task.Delay(20);
                return settingsWindow.CurrentPageKey == key;
            }
            async Task FromGeneral()
            {
                settingsWindow.NavigateTo("basic");
                if (!await Reached("basic")) throw new InvalidOperationException($"Could not enter general settings before the classroom gesture: {settingsWindow.CurrentPageKey}");
            }
            void NewGesture()
            {
                press = ClockCenter();
                source = Hit(press);
                pointer = Mouse();
            }
            (int Seen, bool Handled, bool Captured) DoubleClick()
            {
                NewGesture();
                var count = 0; var handled = false;
                void Observed(object? _, TappedEventArgs e) { count++; handled = e.Handled; }
                window.AddHandler(InputElement.DoubleTappedEvent, Observed, RoutingStrategies.Bubble, true);
                try
                {
                    Press(); var captured = ReferenceEquals(pointer.Captured, source); Release(press);
                    Press(2); Release(press);
                    return (count, handled, captured);
                }
                finally { window.RemoveHandler(InputElement.DoubleTappedEvent, Observed); }
            }
            string Details() => $"route={settingsWindow.CurrentPageKey}, source={source.GetType().Name}#{source.Name}, attached={TopLevel.GetTopLevel(source) == window}, at={press}, island={window.GetIslandScreenRect()}, capture={pointer.Captured}, passthrough={AppServices.Settings.IsClickThrough}";
            check(settingsWindow.CurrentPageKey == "basic", "Releasing the edit handle does not open a classroom shortcut");
            var actual = DoubleClick();
            var home = await Reached("home");
            check(actual is (1, true, false) && home, $"Explicitly interactive classroom double-taps open settings without capturing a move gesture: events={actual.Seen}, handled={actual.Handled}, capturedOnPress={actual.Captured}; {Details()}");
            AppServices.Settings.IsClickThrough = true; AppServices.SaveSettings(); await Settle();
            await FromGeneral(); NewGesture(); Press(); Move(new(press.X + 30, press.Y + 30)); Release(press);
            var ignored = DoubleClick(); await Settle();
            check(ignored is (1, false, false) && At(target) && settingsWindow.CurrentPageKey == "basic", $"Click-through prevents both dragging and shortcut activation: events={ignored.Seen}, handled={ignored.Handled}, capturedOnPress={ignored.Captured}; {Details()}");
            AppServices.Settings.IsClickThrough = false; AppServices.SaveSettings(); await Settle();
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ClassNex.exe"))
                { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true };
            foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("CLASSNEX_REVIEW") || k.StartsWith("CLASSNEX_VERIFY")).ToArray()) info.Environment.Remove(key);
            info.Environment["CLASSNEX_REVIEW_CLOCK_RESTART"] = "1";
            using var other = Process.Start(info) ?? throw new InvalidOperationException("Could not restart the isolated client");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            try { await other.WaitForExitAsync(timeout.Token); }
            finally { if (!other.HasExited) other.Kill(); }
            var resultFile = Path.Combine(fixture, "clock-restart.json");
            if (!File.Exists(resultFile)) throw new InvalidOperationException($"The isolated clock process exited with code {other.ExitCode} without writing its result: {resultFile}");
            var result = await File.ReadAllTextAsync(resultFile);
            using var proof = JsonDocument.Parse(result);
            check(other.ExitCode == 0 && proof.RootElement.GetProperty("Passed").GetBoolean(), "Separate client process reopened saved clock, date order and visible island location: " + result);
            await ReviewEdgePlacementAsync(window, work, check);
            window.ResetPlacement(); await Settle();
            check(At(MainWindow.ClampIsland(work, window.GetIslandScreenRect()!.Value.Size, null)) && AppServices.Settings.MainWindowLeft == 80 && AppServices.Settings.MainWindowTop == 80,
                "Reset placement restores and saves top center without changing the component order");
            check(MainWindow.ClampIsland(new(-1920, -180, 1920, 1040), new(460, 86), new PixelPoint(-2060, 830)) == new PixelPoint(-1920, 774),
                "Saved negative monitor origins and resized island extents are clamped to the correct working area");
        }
        finally
        {
            AppServices.Settings.Widgets = original;
            AppServices.Settings.MainWindowLeft = initialLeft;
            AppServices.Settings.MainWindowTop = initialTop;
            AppServices.Settings.IsClickThrough = initialPassthrough;
            AppServices.Settings.HoverOpacity = initialHover;
            widgets.Save();
            window.ApplySettings();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        double MeasureClock() { var text = Text("ClockValue")!; text.Measure(Size.Infinity); return text.DesiredSize.Width; }
        async Task Settle() { window.UpdateLayout(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }
    }

    private static async Task ReviewEdgePlacementAsync(MainWindow window, PixelRect work, Action<bool, string> check)
    {
        var corner = new PixelPoint(work.Right - 1, work.Bottom - 1);
        PixelPoint requested = default, shown = default;
        await MoveClassroomInEditorAsync(window, (island, down) =>
        {
            requested = island.Position + (corner - down);
            return corner;
        }, island =>
        {
            shown = island.Position;
            var islandOnly = MainWindow.ClampIsland(work, island.Size, requested);
            var toolbar = window.FindControl<Panel>("EditBarHost")!;
            var toolbarEnd = toolbar.PointToScreen(new Point(toolbar.Bounds.Width, toolbar.Bounds.Height));
            check(shown.Y < islandOnly.Y && toolbarEnd.X <= work.Right && toolbarEnd.Y <= work.Bottom,
                $"Bottom-right drag keeps the editor toolbar on-screen: island-only={islandOnly}, displayed={shown}, toolbar-end={toolbarEnd}, work-area={work}");
        });
        var stored = SettingsService.Load();
        var after = window.GetIslandScreenRect()?.Position;
        check(stored.MainWindowLeft == shown.X && stored.MainWindowTop == shown.Y && after == shown,
            $"After a bottom-right drag, the visible island keeps its released position when edit mode closes: released={shown}, saved={stored.MainWindowLeft},{stored.MainWindowTop}, after={after}");
    }

    private static Task MoveClassroomInEditorAsync(MainWindow window, PixelPoint target, Action<PixelRect>? moved = null) =>
        MoveClassroomInEditorAsync(window, (island, down) => down + (target - island.Position), moved);

    private static async Task MoveClassroomInEditorAsync(MainWindow window, Func<PixelRect, PixelPoint, PixelPoint> destination, Action<PixelRect>? moved)
    {
        window.EnterEditMode();
        try
        {
            var began = Stopwatch.GetTimestamp();
            var handle = window.GetVisualDescendants().OfType<Control>().Single(c => c.Name == "MoveIslandHandle");
            PixelPoint down;
            Control hit;
            while (true)
            {
                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                down = handle.PointToScreen(new Point(handle.Bounds.Width / 2, handle.Bounds.Height / 2));
                var local = window.PointToClient(down);
                var candidate = window.InputHitTest(local) as Control;
                if (handle.IsEffectivelyVisible && candidate?.GetSelfAndVisualAncestors().Contains(handle) == true)
                {
                    hit = candidate;
                    break;
                }
                if (Stopwatch.GetElapsedTime(began).TotalSeconds >= 2)
                    throw new InvalidOperationException($"Edit handle missed at {down}, client={local}/{window.ClientSize}, hit={candidate?.GetType().Name}#{candidate?.Name}, handle={handle.Bounds}, island={window.GetIslandScreenRect()}, dpi={window.RenderScaling}");
                await Task.Delay(16);
            }
            var island = window.GetIslandScreenRect() ?? throw new InvalidOperationException("The edit island has no screen position");
            var up = destination(island, down);
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, window, window.PointToClient(down), 1,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            try
            {
                if (pointer.Captured is not InputElement captured) throw new InvalidOperationException("Edit handle did not capture its pointer");
                captured.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, captured, pointer, window, window.PointToClient(up), 2,
                    new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
                moved?.Invoke(window.GetIslandScreenRect()!.Value);
            }
            finally
            {
                var recipient = pointer.Captured as InputElement ?? hit;
                recipient.RaiseEvent(new PointerReleasedEventArgs(recipient, pointer, window, window.PointToClient(up), 3,
                    new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            }
        }
        finally
        {
            window.ExitEditMode();
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
        }
    }

    private static string? ClockContent(TextBlock? clock) => clock is null ? null :
        clock.Inlines is { Count: > 0 } runs ? string.Concat(runs.OfType<Run>().Select(r => r.Text)) : clock.Text;

    internal static async void CheckClockRestart(MainWindow window)
    {
        if (Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_CLOCK_RESTART") != "1") return;
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(directory, ".classnex-review"))) return;
        var passed = false;
        var resultFile = Path.Combine(directory, "clock-restart.json");
        try
        {
            var ready = Stopwatch.StartNew();
            while (!await Dispatcher.UIThread.InvokeAsync(() => window.GetIslandScreenRect() is { Width: > 0, Height: > 0 } && window.Bounds.Width > 0))
            {
                if (ready.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("The reopened clock window did not lay out its island.");
                await Task.Delay(40);
            }
            await Task.Delay(800);
            var result = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var host = window.FindControl<StackPanel>("WidgetHost")!;
                var order = AppServices.Settings.Widgets.Where(w => w.IsEnabled).OrderBy(w => w.Order).Select(w => w.Type).ToArray();
                var time = host.Children.FirstOrDefault() as TextBlock;
                var date = host.Children.LastOrDefault()?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "DateValue");
                var saved = new PixelPoint((int)AppServices.Settings.MainWindowLeft, (int)AppServices.Settings.MainWindowTop);
                var actual = window.GetIslandScreenRect()!.Value;
                passed = actual.Position == saved && order.SequenceEqual(["clock", "date"]) && host.Children.Count == 2 && time?.Name == "ClockValue" && ClockContent(time)?.Length == 5 && date?.IsEffectivelyVisible == true;
                using var frame = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, window.RenderScaling), new Vector(96, 96) * window.RenderScaling);
                frame.Render(window);
                frame.Save(Path.Combine(directory, "clock-restart.png"));
                return JsonSerializer.Serialize(new { Passed = passed, Order = order, Date = date?.Text, Time = ClockContent(time), ActualLeft = actual.X, ActualTop = actual.Y, SavedLeft = saved.X, SavedTop = saved.Y });
            });
            await File.WriteAllTextAsync(resultFile, result);
        }
        catch (Exception ex)
        {
            passed = false;
            await File.WriteAllTextAsync(resultFile, JsonSerializer.Serialize(new { Passed = false, Error = ex.ToString() }));
        }
        finally { await Dispatcher.UIThread.InvokeAsync(() => (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(passed ? 0 : 1)); }
    }

    private static void CheckIslandRegion(MainWindow window, Action<bool, string> check)
    {
        var island = window.GetIslandScreenRect()!.Value;
        var region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) throw new InvalidOperationException("Cannot inspect classroom input region");
        try
        {
            var corner = island.Position - window.Position;
            var handle = window.TryGetPlatformHandle()!.Handle;
            check((GetWindowLongW(handle, -20) & 0x08000000) != 0, "The interactive island does not steal activation from the foreground classroom app");
            check(GetWindowRgn(handle, region) > 0 &&
                PtInRegion(region, corner.X + island.Width / 2, corner.Y + island.Height / 2) &&
                !PtInRegion(region, corner.X, corner.Y) && !PtInRegion(region, corner.X - 2, corner.Y + island.Height / 2),
                "The native input region includes the island and excludes its rounded corner and transparent desktop margin");
        }
        finally { DeleteObject(region); }
    }

    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int x, int y, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint region);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern int GetWindowLongW(nint window, int index);
}
#endif
