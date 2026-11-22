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
            var upcoming = AppServices.Day.Read(DateTime.Now).Next;
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
            AppServices.SaveSettings();
            await Settle();
            var work = window.Screens.Primary!.WorkingArea;
            var initial = window.GetIslandScreenRect()!.Value;
            bool At(PixelPoint target) => window.GetIslandScreenRect() is { } rect && Math.Abs(rect.X - target.X) <= 1 && Math.Abs(rect.Y - target.Y) <= 1;
            check(At(MainWindow.ClampIsland(work, initial.Size, null)), "80/80 starts the visible island at the working area's top center");
            var source = Text("ClockValue")!;
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var press = source.PointToScreen(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2));
            var intended = new PixelPoint(work.X + work.Width / 6, work.Y + work.Height / 5);
            var target = MainWindow.ClampIsland(work, initial.Size, intended);
            var end = new PixelPoint(press.X + target.X - initial.X, press.Y + target.Y - initial.Y);
            void Press() => source.RaiseEvent(new PointerPressedEventArgs(source, pointer, window, window.PointToClient(press), 1,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
            void Move(PixelPoint point) => source.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, source, pointer, window, window.PointToClient(point), 2,
                new(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other), KeyModifiers.None));
            void Release(PixelPoint point) => source.RaiseEvent(new PointerReleasedEventArgs(source, pointer, window, window.PointToClient(point), 3,
                new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));
            Press(); Move(new PixelPoint(press.X + 1, press.Y + 1)); Release(press);
            check(AppServices.Settings.MainWindowLeft == 80 && AppServices.Settings.MainWindowTop == 80 && At(initial.Position), "A click with one pixel of jitter does not move the island");
            Press(); Move(end);
            check(AppServices.Settings.MainWindowLeft == 80 && AppServices.Settings.MainWindowTop == 80 && At(target), "Dragging moves the visible island without committing intermediate positions");
            Release(end); await Settle();
            check(AppServices.Settings.MainWindowLeft == target.X && AppServices.Settings.MainWindowTop == target.Y && At(target), "Releasing the pointer commits its screen-pixel anchor once");
            CheckIslandRegion(window, check);
            await shot(window, "overlay-dragged", mode, dpi);
            void DoubleTap() => source.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                new PointerReleasedEventArgs(source, pointer, window, window.PointToClient(press), 4,
                    new(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left)));
            var settingsWindow = ((App)Current!)._desktop!.Settings!;
            settingsWindow.NavigateTo("basic"); DoubleTap(); await Settle();
            check(settingsWindow.CurrentPageKey == "basic", "Dragging does not trigger a settings shortcut on release");
            press = source.PointToScreen(new Point(source.Bounds.Width / 2, source.Bounds.Height / 2));
            Press(); Release(press); DoubleTap(); await Settle();
            check(settingsWindow.CurrentPageKey == "home", "Double-tapping the classroom routes to the real settings home");
            AppServices.Settings.IsClickThrough = true; AppServices.SaveSettings(); await Settle();
            settingsWindow.NavigateTo("basic"); Press(); Move(new(press.X + 30, press.Y + 30)); Release(press); DoubleTap(); await Settle();
            check(At(target) && settingsWindow.CurrentPageKey == "basic", "Click-through prevents both dragging and shortcut activation");
            AppServices.Settings.IsClickThrough = false; AppServices.SaveSettings(); await Settle();
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "ClassNex.exe"))
                { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true };
            foreach (var key in info.Environment.Keys.Where(k => k.StartsWith("CLASSNEX_REVIEW") || k.StartsWith("CLASSNEX_VERIFY")).ToArray()) info.Environment.Remove(key);
            info.Environment["CLASSNEX_REVIEW_CLOCK_RESTART"] = "1";
            using var other = Process.Start(info) ?? throw new InvalidOperationException("Could not restart the isolated client");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            try { await other.WaitForExitAsync(timeout.Token); }
            finally { if (!other.HasExited) other.Kill(); }
            var result = await File.ReadAllTextAsync(Path.Combine(fixture, "clock-restart.json"));
            using var proof = JsonDocument.Parse(result);
            check(other.ExitCode == 0 && proof.RootElement.GetProperty("Passed").GetBoolean(), "Separate client process reopened saved clock, date order and visible island location: " + result);
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
            widgets.Save();
            window.ApplySettings();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        double MeasureClock() { var text = Text("ClockValue")!; text.Measure(Size.Infinity); return text.DesiredSize.Width; }
        async Task Settle() { window.UpdateLayout(); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }
    }

    private static string? ClockContent(TextBlock? clock) => clock is null ? null :
        clock.Inlines is { Count: > 0 } runs ? string.Concat(runs.OfType<Run>().Select(r => r.Text)) : clock.Text;

    internal static async void CheckClockRestart(MainWindow window)
    {
        if (Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_CLOCK_RESTART") != "1") return;
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
        if (!File.Exists(Path.Combine(directory, ".classnex-review"))) return;
        var passed = false;
        try
        {
            await Task.Delay(800);
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
            await File.WriteAllTextAsync(Path.Combine(directory, "clock-restart.json"), JsonSerializer.Serialize(new { Passed = passed, Order = order, Date = date?.Text, Time = ClockContent(time), ActualLeft = actual.X, ActualTop = actual.Y, SavedLeft = saved.X, SavedTop = saved.Y }));
        }
        finally { (Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(passed ? 0 : 1); }
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
