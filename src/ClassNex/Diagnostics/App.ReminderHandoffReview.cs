#if DEBUG
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Styles;
using ClassNex.Views;

namespace ClassNex;

public partial class App
{
    private static async Task ReviewReminderHandoffAsync(MainWindow window, Func<Window, string, string, int, Task> shot, Action<Window, string, string, int> captureFrame, Action<bool, string> check, string mode, int dpi)
    {
        T Get<T>(string name) where T : Control => window.FindControl<T>(name) ?? throw new InvalidOperationException(name);
        var normal = Get<Border>("CardContent");
        var island = Get<Border>("IslandBackground");
        var mask = Get<Border>("NotificationMask");
        var title = Get<TextBlock>("NotificationMaskText");
        var overlay = Get<Border>("NotificationOverlay");
        var layout = Get<Grid>("NotificationDetailLayout");
        var slides = Get<ListBox>("NotificationSlides");
        var caption = Get<TextBlock>("NotificationPlainCaption");
        var subject = Get<TextBlock>("NotificationOverlaySubject");
        var teacher = Get<TextBlock>("NotificationOverlayTeacher");
        var time = Get<TextBlock>("NotificationOverlayTime");
        var message = Get<TextBlock>("NotificationOverlayMessagePage");
        var simple = Get<TextBlock>("NotificationSimpleMessage");
        static async Task Until(Func<bool> ready, Func<string> problem)
        {
            var began = Stopwatch.GetTimestamp();
            while (!ready())
            {
                if (Stopwatch.GetElapsedTime(began) > TimeSpan.FromSeconds(5)) throw new InvalidOperationException(problem());
                await Task.Delay(16);
            }
        }
        static bool HasTail(TextBlock t) => t.Text is { Length: > 0 } text && t.TextLayout.TextLines.LastOrDefault() is { } last &&
            last.FirstTextSourceIndex + last.Length >= text.Length && !t.TextLayout.TextLines.Any(line => line.HasCollapsed);
        static bool Inside(TextBlock t, Control host) => t.IsEffectivelyVisible && t.TranslatePoint(default, host) is { } p && p.X >= -1 && p.Y >= -1 &&
            p.X + t.Bounds.Width <= host.Bounds.Width + 1 && p.Y + t.TextLayout.Height <= host.Bounds.Height + 1 && HasTail(t);
        static string State(TextBlock t, Control host) => $"{t.Name}: origin {t.TranslatePoint(default, host)}, bounds {t.Bounds}, lines {t.TextLayout.TextLines.Count}, ink height {t.TextLayout.Height:0.#}, tail {HasTail(t)}, viewport {host.Bounds}";
        static bool SettledSlide(TextBlock t) => t.GetVisualAncestors().OfType<ContentPresenter>().Any(p => p.Name == "ContentPresenter" && p.IsEffectivelyVisible && p.Opacity > .99);
        bool Restored() => !mask.IsVisible && !overlay.IsVisible && normal.Clip is null && normal.Opacity >= .999 &&
            Math.Abs(island.Bounds.Width - Math.Ceiling(normal.Bounds.Width + normal.Margin.Left + normal.Margin.Right)) <= 1;
        var count = Math.Clamp((int)Math.Ceiling(window.MaxWidth / window.ReminderScale / 16), 25, 350);
        WidgetConfig? changedDuringReturn = null;
        string GradeNotice(string text)
        {
            if (text.Length >= count * 1.5) return text;
            var notice = new System.Text.StringBuilder(text).Append(" 参加班级：");
            for (var room = 1; notice.Length < count * 1.5; room++)
            {
                if (room > 1) notice.Append('、');
                notice.Append("七年级").Append(room).Append('班');
            }
            return notice.Append('。').ToString();
        }
        try
        {
            window.ShowNotificationMask(GradeNotice("七年级同学请注意：下午第一节体育与健康课改在东操场进行。请在预备铃响前换好运动鞋，带上水杯，到各班标志牌后集合；如遇下雨，请留在教室等候老师通知。"));
            await Until(() => mask.IsVisible && title.TextLayout.TextLines.Count > 1 && Inside(title, mask) &&
                Get<Border>("NotificationMaskContent").Opacity >= .995, () => State(title, mask));
            check(mask.Bounds.Height > 40 && Inside(title, mask), "Long mask titles expand within the working area; the final character and both text lines remain visible");
            await shot(window, "reminder-long-title", mode, dpi);
            var longMessage = GradeNotice("请收好课本，换好运动鞋，带上水杯从东侧楼梯下楼。到操场后先找本班标志牌，跟体育委员做热身；接棒练习听哨声后开始，暂时不能参加运动的同学请先向老师报到。");
            window.ShowNotificationOverlay("七年级运动会 · 下午场地调整", GradeNotice("体育与健康：校园接力赛——起跑与弯道跑、交接棒练习、四人接力及赛后交流。第一轮在直道分组练习，第二轮到弯道交接区，最后一轮按班级完成计时接力。"),
                "七年级体育备课组 · 张老师（起跑区）、陈老师（交接区）、王老师（终点计时）", "下午第一节 13:30–14:10", "下节课是", longMessage, true);
            var labels = new[] { caption, subject, teacher, time };
            await Until(() => !mask.IsVisible && SettledSlide(subject) && labels.All(t => Inside(t, overlay)), () => string.Join("; ", labels.Select(t => State(t, overlay))));
            check(subject.TextLayout.TextLines.Count > 1 && overlay.Bounds.Height > 40 && labels.All(t => Inside(t, overlay)), "A long course, caption, teacher and time range keep their complete text inside the rounded provider");
            await shot(window, "reminder-wrapped-course", mode, dpi);
            var size = overlay.Bounds.Size;
            var center = window.GetIslandCenterOnScreen();
            slides.SelectedIndex = 1;
            await Until(() => SettledSlide(message) && Inside(message, overlay), () => State(message, overlay));
            check(message.TextLayout.TextLines.Count > 1 && overlay.Bounds.Size == size && center == window.GetIslandCenterOnScreen(), "Flipping to a multi-line message does not resize or move the centered island");
            await shot(window, "reminder-wrapped-message", mode, dpi);
            window.HideNotificationMask();
            if (DesktopMotion.IsEnabled)
            {
                await Until(() => overlay.IsVisible && overlay.Opacity is > .02 and < .98 && normal.Opacity < .001,
                    () => $"Missing outgoing-only frame: detail {overlay.Opacity}, classroom {normal.Opacity}");
                check(Math.Abs(layout.Width - size.Width) <= 1, "The outgoing detail keeps its measured width until its real fade ends");
            }
            else check(!overlay.IsVisible && !mask.IsVisible && normal.Opacity == 1, "Reduced motion returns directly to classroom content without a blank mask");
            window.ShowNotificationMask("放学");
            window.ShowNotificationOverlay("", "", "", "", message: GradeNotice("今天的课程已经结束。请值日小组关好投影、窗户和教室电灯；其他同学检查水杯和书包，按放学路线离校。参加课后篮球社的同学请带好运动鞋，在体育馆门口等候带队老师。"));
            await Until(() => !mask.IsVisible && simple.IsEffectivelyVisible && simple.TextLayout.TextLines.Count > 1 && Inside(simple, overlay), () => State(simple, overlay));
            check(overlay.Opacity >= .999 && Inside(simple, overlay), "A replacement dismissal survives the cancelled exit and wraps instead of trimming the last line");
            await shot(window, "reminder-wrapped-dismissal", mode, dpi);
            var start = island.Bounds.Width;
            var goal = Math.Ceiling(normal.Bounds.Width + normal.Margin.Left + normal.Margin.Right);
            window.HideNotificationMask();
            if (DesktopMotion.IsEnabled && start > goal + 5)
            {
                await Until(() => !overlay.IsVisible && normal.Opacity > .98 && island.Bounds.Width < start - 1 && island.Bounds.Width > goal + 1,
                    () => $"No intermediate return size observed: {start} -> {island.Bounds.Width} -> {goal}");
                check(normal.IsEffectivelyVisible && normal.Clip is not null, "The fully visible classroom owns the shrinking island and remains clipped to its moving outline");
                var oldNormal = normal.Bounds.Width;
                changedDuringReturn = AppServices.Widgets.Add("text");
                changedDuringReturn.Text = "实时课堂宽度";
                AppServices.Widgets.Save();
                await Until(() => normal.Bounds.Width > oldNormal + 8 && Restored(),
                    () => $"Handoff did not follow the new live widget: original {oldNormal}, now {normal.Bounds}, island {island.Bounds}, clip {normal.Clip}");
                check(normal.Bounds.Width > oldNormal + 8 && Restored(), "A real widget change during the return animation retargets the new classroom width and releases notification ownership");
                AppServices.Widgets.Remove(changedDuringReturn);
                changedDuringReturn = null;
            }
            await Until(Restored, () => $"Classroom handoff did not finish: {island.Bounds}, content {normal.Bounds}, remaining clip {normal.Clip}, mask {mask.IsVisible}, detail {overlay.IsVisible}");
            check(Restored(), "After the handoff no stale clip, mask, transparent detail or forced reminder width remains");

            if (DesktopMotion.IsEnabled)
            {
                window.ShowNotificationMask("下课");
                window.ShowNotificationOverlay("本节课间长 10 分钟", "物理", "陈老师", "10:32–11:12", "下节课是");
                await Until(() => !mask.IsVisible && SettledSlide(subject) && normal.Opacity < .001, () => "The detail did not settle before the handoff probe");
                (double Old, double New)? outgoing = null, incoming = null;
                double brightestOverlap = 0;
                var backdropOpacity = island.Opacity;
                bool continuousBackdrop = true;
                int samples = 0;
                var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Frame(TimeSpan _)
                {
                    if (finished.Task.IsCompleted) return;
                    try { if (Sample()) finished.TrySetResult(); else window.RequestAnimationFrame(Frame); }
                    catch (Exception ex) { finished.TrySetException(ex); }
                }
                bool Sample()
                {
                    var old = overlay.IsVisible ? overlay.Opacity : 0;
                    var next = normal.Opacity;
                    brightestOverlap = Math.Max(brightestOverlap, Math.Min(old, next));
                    continuousBackdrop &= island.IsEffectivelyVisible && backdropOpacity > 0 && Math.Abs(island.Opacity - backdropOpacity) < .001 && island.Background != null && island.Bounds.Height > 0;
                    samples++;
                    if (outgoing is null && old is > .15 and < .95 && next < .001)
                    {
                        outgoing = (old, next);
                        captureFrame(window, "reminder-handoff-outgoing", mode, dpi);
                    }
                    if (incoming is null && next is > .15 and < .95 && old < .001)
                    {
                        incoming = (old, next);
                        captureFrame(window, "reminder-handoff-incoming", mode, dpi);
                    }
                    return !overlay.IsVisible && next > .99;
                }
                window.RequestAnimationFrame(Frame);
                window.HideNotificationMask();
                try { await finished.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                finally { finished.TrySetCanceled(); }
                check(outgoing.HasValue && incoming.HasValue && samples >= 4 && brightestOverlap < .001 && continuousBackdrop,
                    $"Two real fade-through frames keep the backdrop visible without overlapping text; outgoing {outgoing}, incoming {incoming}, maximum shared opacity {brightestOverlap:0.000}, continuous backdrop {continuousBackdrop}, {samples} samples");
                await Until(Restored, () => "Classroom size did not settle after recording the fade-through");
            }

            await ReviewReminderWheelAsync(window, check);
        }
        finally
        {
            if (changedDuringReturn != null) AppServices.Widgets.Remove(changedDuringReturn);
            window.HideNotificationMask();
        }
        await Until(Restored, () => "Final classroom return did not settle");
    }
}
#endif
