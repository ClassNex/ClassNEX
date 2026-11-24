#if DEBUG
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaFluentUI.Controls;
using ClassNex.Services;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private sealed class ClassroomReviewClock : TimeProvider
    {
        private readonly long _start = Stopwatch.GetTimestamp();
        private readonly DateTimeOffset _schoolMorning = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Local);
        public override DateTimeOffset GetUtcNow() => _schoolMorning.ToUniversalTime() + Stopwatch.GetElapsedTime(_start);
    }

    private sealed record Capture(string Surface, string Theme, double Scaling, int Width, int Height, int VisibleText);

    private async Task ReviewWindowsAsync(string directory, IClassicDesktopStyleApplicationLifetime desktop)
    {
        var captures = new List<Capture>();
        var checks = new List<string>();
        var ticket = Stopwatch.StartNew();
        var exitCode = 0;
        Directory.CreateDirectory(directory);
        void Check(bool passed, string description)
        {
            if (!passed) throw new InvalidOperationException(description);
            checks.Add(description);
        }
        try
        {
            Check(AccountService.Current is null && !AppServices.Settings.CentralManagement.IsEnabled,
                "Anonymous fixture, no production account or management connection");
            Check(AppServices.Settings.SampleSeedVersion == 3 && Math.Abs(AppServices.Settings.BackgroundOpacity - .55) < .001 &&
                AppServices.Schedule.Profile.Schedules.Any(s => s.Name == "教学日"), "Alpha settings and edited portable timetable survive first startup");
            var overlay = _mainWindow ?? throw new InvalidOperationException("No classroom window");
            if (Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_REMINDERS_ONLY") == "1")
            {
                SetReviewDpi(overlay, 144);
                await Dispatcher.UIThread.InvokeAsync(() => overlay.UpdateLayout(), DispatcherPriority.Background);
                overlay.ResetPlacement();
                await ReviewRemindersAsync(overlay, Shot, Check);
                return;
            }
            await ReviewVoiceLifecycleAsync(Check);
            await Task.Delay(500);
            OpenSettings();
            var settings = _desktop!.Settings ?? throw new InvalidOperationException("No WinUI settings window");
            await ReviewOobePreviewAsync(desktop, overlay, settings, Shot, Check);
            await _desktop.ReviewSettingsLayoutAsync(directory, Check);
            OpenProfileEditor();
            var editor = _profileEditor!;
            OpenLoginWindow();
            var login = _loginWindow!;
            Check(!AccountService.FlowActive, "Opening sign-in does not start browser authorization");
            foreach (var dpi in new[] { 96, 120, 144 })
            {
                foreach (var window in new Window[] { overlay, editor, login }) SetReviewDpi(window, dpi);
                await Task.Delay(180);
                foreach (var mode in new[] { "light", "dark" })
                {
                    ApplyTheme(mode);
                    Check(Math.Abs(overlay.RenderScaling - dpi / 96d) < .001, $"Classroom backend accepted {dpi} DPI");
                    await Shot(overlay, "classroom", mode, dpi);
                    editor.SelectTab(0);
                    await Shot(editor, "schedule", mode, dpi);
                    await Shot(login, "login", mode, dpi);
                }
            }
            login.Close(); editor.Close();
            await ReviewClockLayoutAsync(overlay, Shot, Check);
            settings.NavigateTo("notification");
            await ReviewRemindersAsync(overlay, Shot, Check);
            settings.NavigateTo("home");
            settings.Activate();
            Check(_loginWindow is null && _profileEditor is null, "Auxiliary window references released on close");
            async Task Shot(Window window, string name, string mode, int dpi)
            {
                await Task.Delay(220);
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
                var bounds = window.Bounds;
                var scale = window.RenderScaling;
                var size = PixelSize.FromSize(bounds.Size, scale);
                if (!window.IsVisible || size.Width < 24 || size.Height < 24) throw new InvalidOperationException($"No live content: {name}");
                using var bitmap = new RenderTargetBitmap(size, new Vector(dpi, dpi));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"{name}-{mode}-{dpi}.png"));
                var text = window.GetVisualDescendants().OfType<TextBlock>().Count(b => b.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(b.Text));
                if (text == 0) throw new InvalidOperationException($"No readable text: {name}");
                captures.Add(new Capture(name, mode, scale, size.Width, size.Height, text));
            }
        }
        catch (Exception ex) { checks.Add(ex.ToString()); exitCode = 1; }
        finally
        {
            using var process = Process.GetCurrentProcess();
            var report = new { Passed = exitCode == 0, Checks = checks, Captures = captures,
                ElapsedMs = ticket.ElapsedMilliseconds, WorkingSetBytes = process.WorkingSet64, ManagedBytes = GC.GetTotalMemory(false) };
            await File.WriteAllTextAsync(Path.Combine(directory, "review.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            if (exitCode != 0 || Environment.GetEnvironmentVariable("CLASSNEX_REVIEW_KEEP_OPEN") != "1")
                desktop.Shutdown(exitCode);
        }
    }

    private static void SetReviewDpi(Window window, int dpi)
    {
        var handle = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("Missing native handle");
        if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("Cannot measure native window");
        var ratio = dpi / (96 * window.RenderScaling);
        rect.Right = rect.Left + (int)Math.Round((rect.Right - rect.Left) * ratio);
        rect.Bottom = rect.Top + (int)Math.Round((rect.Bottom - rect.Top) * ratio);
        _ = SendMessageW(handle, 0x02E0, dpi | (dpi << 16), ref rect);
    }

    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out RectNative rect);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint msg, nint dpi, ref RectNative rect);
}
#endif
