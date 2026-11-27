#if DEBUG
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using ClassNex.Core.Abstractions;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewReminderGeometryAsync(MainWindow window, Func<Window, string, string, int, Task> shot, Action<bool, string> check)
    {
        var settings = AppServices.Settings;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "..", ".classnex-review")) || AccountService.Current != null || settings.CentralManagement.IsEnabled)
            throw new InvalidOperationException("Reminder geometry review requires an anonymous portable fixture.");
        var originalDpi = (int)Math.Round(window.RenderScaling * 96);
        var originalMotion = DesktopMotion.IsEnabled;
        var original = (settings.Widgets, settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock, settings.MainWindowScale, settings.IsClickThrough);
        var work = window.Screens.Primary?.WorkingArea ?? throw new InvalidOperationException("No primary working area");
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? throw new InvalidOperationException("No classroom HWND");
        T Get<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new InvalidOperationException(name);
        var island = Get<Border>("IslandBackground");
        var classroom = Get<Border>("CardContent");
        var mask = Get<Border>("NotificationMask");
        var icons = Get<StackPanel>("NotificationMaskIcons");
        var titleFace = Get<Border>("NotificationMaskContent");
        var detail = Get<Border>("NotificationOverlay");
        var lesson = Get<WrapPanel>("NotificationLesson");
        var prefix = Get<TextBlock>("NotificationOverlayPrefix");
        var subject = Get<TextBlock>("NotificationOverlaySubject");
        var time = Get<TextBlock>("NotificationOverlayTime");
        var mode = window.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";
        var cases = new (string Key, string Title, NotificationOverlay Body, int? Seconds, string Caption)[]
        {
            ("test", "测试提醒", new("这是一条测试提醒", "通知系统工作正常", "", "10:03"), null, "NotificationPlainCaption"),
            ("prepare", "准备上课", new("", "体育与健康", "", "10:32-11:12", "下节课是"), 1411, "NotificationOverlayLeft"),
            ("break", "下课", new("", "体育与健康", "", "10:32-11:12", "下节课是") { Break = ("课间", "10 分钟") }, null, "NotificationBreakCaption")
        };

        static PixelRect Box(Control control)
        {
            var start = control.PointToScreen(default);
            var end = control.PointToScreen(new Point(control.Bounds.Width, control.Bounds.Height));
            return new PixelRect(start.X, start.Y, end.X - start.X, end.Y - start.Y);
        }
        static double ScreenScale(Control control) => (control.PointToScreen(new Point(100, 0)).X - control.PointToScreen(default).X) / 100d;
        PixelRect Native() => GetWindowRect(hwnd, out var r) ? new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : throw new InvalidOperationException("GetWindowRect failed");
        static bool ContainsX(PixelRect outer, PixelRect inner) => inner.Width > 0 && inner.X >= outer.X - 2 && inner.Right <= outer.Right + 2;
        static bool Contains(PixelRect outer, PixelRect inner) => ContainsX(outer, inner) && inner.Height > 0 && inner.Y >= outer.Y - 2 && inner.Bottom <= outer.Bottom + 2;
        static bool Close(double a, double b) => Math.Abs(a - b) <= 2;
        static bool Same(PixelRect a, PixelRect b) => Close(a.X, b.X) && Close(a.Y, b.Y) && Close(a.Right, b.Right) && Close(a.Bottom, b.Bottom);
        static double CenterX(PixelRect r) => r.X + r.Width / 2d;
        static bool Readable(TextBlock text) => text.IsEffectivelyVisible && text.TextLayout.TextLines.Count == 1 && !text.TextLayout.TextLines[0].HasCollapsed;
        static bool WholeLineInside(TextBlock text, PixelRect viewport)
        {
            if (!Readable(text)) return false;
            var line = text.TextLayout.TextLines[0];
            var length = string.IsNullOrEmpty(text.Text) ? text.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Sum(run => run.Text?.Length ?? 0) ?? 0 : text.Text.Length;
            var start = text.PointToScreen(new Point(text.Padding.Left + line.Start, text.Padding.Top));
            var end = text.PointToScreen(new Point(text.Padding.Left + line.Start + line.Width, text.Padding.Top + line.Height));
            return length > 0 && line.FirstTextSourceIndex + line.Length >= length && Contains(viewport, new(start.X, start.Y, end.X - start.X, end.Y - start.Y));
        }
        bool AtRest() => !mask.IsVisible && !detail.IsVisible && classroom.IsEffectivelyVisible && classroom.Opacity >= .999 && classroom.Clip == null &&
            Math.Abs(island.Bounds.Width - Math.Ceiling(classroom.Bounds.Width + classroom.Margin.Left + classroom.Margin.Right)) <= 1;

        async Task<(PixelRect Island, PixelRect Window)> Settle(Func<bool> phase, string name)
        {
            var start = Stopwatch.GetTimestamp();
            (PixelRect Island, PixelRect Window) previous = default;
            var stable = 0;
            do
            {
                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
                var current = (Island: Box(island), Window: Native());
                stable = phase() && current.Island.Width > 0 && current.Window.Width > 0 && current == previous ? stable + 1 : 0;
                if (stable >= 4) return current;
                previous = current;
                await Task.Delay(22);
            } while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(5));
            throw new InvalidOperationException($"{name}: no stable classroom geometry; island={previous.Island}, HWND={previous.Window}, work={work}, mask={mask.IsVisible}, detail={detail.IsVisible}, classroom={classroom.Bounds}/{classroom.Opacity:0.00}, dpi={window.RenderScaling * 96:0}");
        }

        async Task Probe(string anchor, int index, bool capture = false)
        {
            var sample = cases[index];
            var dpi = (int)Math.Round(window.RenderScaling * 96);
            var name = $"{sample.Key}/{anchor}/{dpi}";
            var baseline = await Settle(AtRest, name + " rest");
            var saved = (settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock);
            var caption = Get<TextBlock>(sample.Caption);
            void CheckPosition((PixelRect Island, PixelRect Window) visible, string phase)
            {
                var r = visible.Island;
                var x = Math.Clamp(anchor switch
                {
                    "default" => CenterX(work) - r.Width / 2d,
                    "import-left" => work.X + 7,
                    "import-right" => work.Right - 9 - r.Width,
                    _ => CenterX(baseline.Island) - r.Width / 2d
                }, work.X, Math.Max(work.X, work.Right - r.Width));
                var y = anchor switch { "default" => work.Y, "import-left" => work.Y + 5, "import-right" => work.Y + 4,
                    _ => Math.Clamp(baseline.Island.Y, work.Y, Math.Max(work.Y, work.Bottom - r.Height)) };
                check(Close(r.X, x) && Close(r.Y, y) && Contains(work, r) && Contains(visible.Window, r),
                    $"{name} {phase}: expected ({x:0.#},{y}), island={r}, HWND={visible.Window}, classroom={baseline.Island}, work={work} (2 screen px)");
            }
            CheckPosition(baseline, "rest");
            if (index == 2) window.ShowNotificationMask(sample.Title, "\uE224", "\uE250");
            else window.ShowNotificationMask(sample.Title);
            var title = await Settle(() => mask.IsEffectivelyVisible && !detail.IsVisible && titleFace.Opacity >= .995, name + " title");
            CheckPosition(title, "title");
            var heading = Box(icons);
            check(Contains(title.Island, heading) && Close(CenterX(heading), CenterX(title.Island)), $"{name}: both icons and title center together, heading={heading}, island={title.Island}");

            window.ShowNotificationOverlay(sample.Body, sample.Seconds, new NotificationOverlayPlayback(TimeSpan.FromMinutes(3)));
            var body = await Settle(() => !mask.IsVisible && detail.IsEffectivelyVisible && subject.IsEffectivelyVisible && caption.IsEffectivelyVisible && lesson.Bounds.Width > 0, name + " detail");
            CheckPosition(body, "detail");
            check(body.Island.Width > baseline.Island.Width + 16 && body.Window.Width > baseline.Window.Width + 16,
                $"{name}: both the island and the native window grew, classroom HWND={baseline.Window}, detail HWND={body.Window}");
            if (anchor is "edge-left" or "edge-right")
                check(anchor == "edge-left" ? Close(body.Island.X, work.X) : Close(body.Island.Right, work.Right), $"{name}: the expanded detail meets the available screen edge");
            var first = string.IsNullOrEmpty(prefix.Text) ? subject : prefix;
            var left = Box(caption); var start = Box(first); var end = Box(time); var group = Box(lesson);
            var scale = ScreenScale(lesson);
            var gap = (start.X - left.Right) / scale;
            var leftInset = left.X - body.Island.X; var rightInset = body.Island.Right - end.Right;
            var viewport = Box(Get<ScrollViewer>("NotificationBodyViewport"));
            var labels = new[] { caption, first, subject, time };
            check(labels.All(text => WholeLineInside(text, body.Island) && WholeLineInside(text, viewport)) && subject.Text == sample.Body.Subject && time.Text == sample.Body.Time &&
                  Contains(body.Island, left) && Contains(body.Island, group) && ContainsX(group, start) && ContainsX(group, Box(subject)) && ContainsX(group, end) &&
                  left.Y < group.Bottom && group.Y < left.Bottom && gap is >= 20 and <= 28 && Close(leftInset, rightInset),
                $"{name}: complete single-line columns, balanced outer insets {leftInset}/{rightInset}px, gap={start.X - left.Right}px/{scale:0.###} measured px per lesson DIP={gap:0.#} DIP; viewport={viewport}, caption={left}, lesson={group}, subject={Box(subject)}, time={end}");
            if (capture) await shot(window, "reminder-geometry-" + sample.Key, mode, dpi);
            if (capture && sample.Seconds.HasValue)
            {
                var count = window.Find<Avalonia.Controls.Documents.Run>("NotificationSeconds") ?? throw new InvalidOperationException("No countdown run");
                window.UpdateNotificationOverlayCountdown(999);
                var narrower = await Settle(() => count.Text == "999" && Readable(caption), name + " three digits");
                CheckPosition(narrower, "three-digit countdown");
                var shortCaption = Box(caption);
                var shortTextFits = WholeLineInside(caption, narrower.Island);
                window.UpdateNotificationOverlayCountdown(1000);
                var wider = await Settle(() => count.Text == "1000" && Readable(caption), name + " four digits");
                CheckPosition(wider, "four-digit countdown");
                check(wider.Island.Width > narrower.Island.Width + 2 && Box(caption).Width > shortCaption.Width + 2 && Same(wider.Island, body.Island) &&
                      WholeLineInside(caption, wider.Island) && shortTextFits && Box(caption).Right < Box(first).X,
                    $"{name}: all digits still fit through 1411 → 999 → 1000; centered island {body.Island} → {narrower.Island} → {wider.Island}, caption {shortCaption} → {Box(caption)}");
            }
            window.HideNotificationMask();
            var after = await Settle(AtRest, name + " return");
            var disk = SettingsService.Load();
            check(Same(after.Island, baseline.Island) && Same(after.Window, baseline.Window) && saved == (settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock) &&
                  saved == (disk.MainWindowLeft, disk.MainWindowTop, disk.ImportedDock),
                $"{name}: dismissal restored the classroom and HWND on all four edges without changing saved placement: island={after.Island}, HWND={after.Window}");
        }

        void Place(double x, double y, ImportedDockPosition? dock = null)
        {
            (settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock) = (x, y, dock);
            AppServices.SaveSettings();
        }
        async Task Dpi(int value)
        {
            SetReviewDpi(window, value);
            await Settle(AtRest, $"{value} DPI classroom");
            check(Math.Abs(window.RenderScaling * 96 - value) < .01 && ScreenScale(window) > 0 && ScreenScale(island) > 0,
                $"Simulated WM_DPICHANGED {value} DPI accepted; measured PointToScreen px/100 DIPs: native client {ScreenScale(window) * 100:0}, island {ScreenScale(island) * 100:0}");
        }
        try
        {
            DesktopMotion.SetReviewEnabled(false);
            window.HideNotificationMask();
            settings.Widgets = [new WidgetConfig { Type = "text", Text = "课程" }];
            settings.MainWindowScale = 1;
            settings.IsClickThrough = false;
            Place(80, 80);
            await Dpi(96);
            await Probe("default", 0);
            Place(80, 80, new(0, 7, 5, -1));
            await Probe("import-left", 1);
            Place(80, 80, new(2, -9, 4, -1));
            await Dpi(144);
            await Probe("import-right", 2);
            var small = Box(island);
            Place(work.X + 2, work.Y + 4);
            await Probe("edge-left", 1);
            Place(work.Right - small.Width - 2, work.Y + 4);
            await Probe("edge-right", 2);
            settings.MainWindowScale = 1.45;
            Place(80, 80);
            await Dpi(120);
            DesktopMotion.SetReviewEnabled(originalMotion);
            var initial = (await Settle(AtRest, "before drag")).Island;
            var intendedX = work.X + 645;
            if (intendedX < work.X + work.Width / 5 || intendedX > work.Right - work.Width / 5 - initial.Width || Math.Abs(intendedX - initial.X) < 15)
                intendedX = initial.X + Math.Max(16, work.Width / 32);
            var target = new PixelPoint(intendedX, work.Y + 4);
            await MoveClassroomInEditorAsync(window, target);
            var dragged = (await Settle(AtRest, "after drag")).Island;
            var persisted = SettingsService.Load();
            check(Close(dragged.X, target.X) && Close(dragged.Y, target.Y) && Close(persisted.MainWindowLeft, target.X) && Close(persisted.MainWindowTop, target.Y) && persisted.ImportedDock == null,
                $"Dragging the editing toolbar handle saved the classroom origin before the three reminders: target={target}, classroom={dragged}, persisted=({persisted.MainWindowLeft},{persisted.MainWindowTop})");
            for (var i = 0; i < cases.Length; i++) await Probe("dragged", i, true);
        }
        finally
        {
            try
            {
                DesktopMotion.SetReviewEnabled(false);
                window.HideNotificationMask();
                (settings.Widgets, settings.MainWindowLeft, settings.MainWindowTop, settings.ImportedDock, settings.MainWindowScale, settings.IsClickThrough) = original;
                SetReviewDpi(window, originalDpi);
                AppServices.SaveSettings();
                await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            }
            finally { DesktopMotion.SetReviewEnabled(null); }
        }
    }
}
#endif
