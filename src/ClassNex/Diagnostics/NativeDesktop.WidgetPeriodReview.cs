using Avalonia.VisualTree;
using ClassNex.Models;
using Ac = Avalonia.Controls;
using Wu = Microsoft.UI.Xaml.Controls;

namespace ClassNex.Services;

internal sealed partial class NativeDesktop
{
    private sealed partial class NativeVerification
    {
        private async Task VerifyWidgetClassroomPeriods()
        {
            var window = host._overlay;
            var original = await Ui(() => AppServices.Settings.Widgets);
            var date = DateOnly.FromDateTime(AppServices.ClassroomNow).AddDays(1);
            var next = date.AddDays(1);
            var schedules = AppServices.LocalSchedule.TemporarySchedules;
            var clock = new WidgetConfig { Type = "clock", Order = 1 };
            var activity = new WidgetConfig { Type = "text", Row = 1, Order = 2, Text = "集合", DisplayWhen = WidgetDisplayWhen.Activity, Presentation = new(Width: 80) };
            var dayOff = new WidgetConfig { Type = "text", Row = 2, Order = 3, Text = "今日无课", DisplayWhen = WidgetDisplayWhen.NoLessonsToday, Presentation = new(Width: 90) };
            static TimeSpan At(int minute) => TimeSpan.FromMinutes(8 * 60 + minute);
            var teaching = new TemporarySchedule(date, "当天作息", [new("语文", At(0), At(40)), new("数学", At(50), At(90))])
            { TimeLayout = [new(ClassTimeKind.Break, "课间", At(40), At(50)), new(ClassTimeKind.Action, "集合", At(43), At(45))] };
            void Tick(DateOnly d, int minute) => window.RefreshWidgetsAt(d.ToDateTime(TimeOnly.FromTimeSpan(At(minute))));
            Ac.Control Part(string name) => window.GetVisualDescendants().OfType<Ac.Control>().Single(c => c.Name == name);
            Ac.Border Surface(WidgetConfig w) => window.GetVisualDescendants().OfType<Ac.Border>().Single(b => ReferenceEquals(b.Tag, w));
            Task Capture(DateOnly day, int minute, string name) => Act("capture-period-" + name, () =>
            {
                Tick(day, minute); window.UpdateLayout();
                var line = Part("LinePanel"); var scale = window.RenderScaling;
                using var image = new Avalonia.Media.Imaging.RenderTargetBitmap(Avalonia.PixelSize.FromSize(line.Bounds.Size, scale), new Avalonia.Vector(96 * scale, 96 * scale));
                image.Render(line); image.Save(Path.Combine(SettingsService.DataDirectory, "desktop-period-" + name + ".png"));
            });
            bool ContentAt(DateOnly d, int minute, bool showClock, bool showActivity, bool noLessons)
            {
                Tick(d, minute);
                return Part("DateValue").IsEffectivelyVisible && Part("DateSeparator").IsEffectivelyVisible == showClock && Part("ClockValue").IsEffectivelyVisible == showClock &&
                    Surface(activity).IsEffectivelyVisible == showActivity && Surface(dayOff).IsEffectivelyVisible == noLessons &&
                    (Surface(activity).Parent as Ac.StackPanel)?.IsVisible == showActivity && (Surface(dayOff).Parent as Ac.StackPanel)?.IsVisible == noLessons;
            }
            bool SurfaceIs(bool visible)
            {
                var line = Part("LinePanel");
                var hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
                return line.Opacity == (visible ? 1 : 0) && line.IsHitTestVisible == visible && hwnd != 0 && ((GetWindowLongW(hwnd, -20) & 0x20) != 0) != visible;
            }
            var cleanDates = await Ui(() => !AppServices.Schedule.Profile.TemporarySchedules.ContainsKey(date) && !AppServices.Schedule.Profile.TemporarySchedules.ContainsKey(next));
            if (!cleanDates) throw new InvalidOperationException("Period review dates must be empty in the disposable classroom");
            var pointerPreferences = await Ui(() => (AppServices.Settings.IsClickThrough, AppServices.Settings.HoverOpacity));
            try
            {
                await Act("arrange-classroom-display-periods", () =>
                {
                    schedules.Apply(teaching); schedules.Apply(new(next, "停课", []) { TimeLayout = [] });
                    AppServices.Settings.IsClickThrough = false;
                    AppServices.Settings.HoverOpacity = 1;
                    AppServices.Settings.Widgets = [new() { Type = "date" }, clock, activity, dayOff];
                    AppServices.SaveSettings(); Settings.SelectWidget(1); Settings.NavigateTo("widget-layout");
                });
                await Check("widget-period-selector-starts-unrestricted", () => Find<Wu.ComboBox>(Page, "WidgetDisplayWhen") is { IsLoaded: true, SelectedIndex: 0, Items.Count: 5 }, 4000);
                await Act("set-clock-to-breaks-only", () => Need<Wu.ComboBox>(Page, "WidgetDisplayWhen").SelectedIndex = (int)WidgetDisplayWhen.Break);
                await Check("widget-period-selection-persists-and-reaches-live-clock", () => SettingsService.Load().Widgets[1].DisplayWhen == WidgetDisplayWhen.Break &&
                    ContentAt(date, 30, false, false, false), 4000);
                await Check("break-shows-clock-and-adjoining-date-divider", () => ContentAt(date, 40, true, false, false));
                await Capture(date, 40, "break");
                await Check("activity-replaces-clock-and-collapses-empty-rows", () => ContentAt(date, 43, false, true, false));
                await Capture(date, 43, "activity");
                await Check("clock-returns-at-end-of-activity", () => ContentAt(date, 45, true, false, false));
                await Check("next-lesson-restores-classroom-row", () => ContentAt(date, 50, false, false, false));
                await Check("empty-day-shows-only-its-conditional-row", () => ContentAt(next, 41, false, false, true));
                await Capture(next, 41, "day-off");
                await Act("change-same-days-activity", () => schedules.Apply(teaching with { TimeLayout = [new(ClassTimeKind.Action, "集合", At(40), At(50))] }));
                await Check("same-day-timetable-edits-update-widget-periods", () => ContentAt(date, 41, false, true, false), 4000);
                await Act("edit-independently-of-display-period", window.EnterEditMode);
                await Check("edit-mode-reveals-widgets-outside-their-period", () =>
                {
                    Tick(date, 30); return window.IsEditMode && Part("ClockValue").IsEffectivelyVisible && Surface(activity).IsEffectivelyVisible && Surface(dayOff).IsEffectivelyVisible;
                }, 4000);
                await Act("leave-display-period-editor", window.ExitEditMode);
                await Check("leaving-edit-mode-reapplies-periods", () => !window.IsEditMode && ContentAt(date, 30, false, false, false), 4000);
                await Act("arrange-only-filtered-widget", () => { Settings.NavigateTo("widgets"); AppServices.Settings.Widgets = [clock]; AppServices.SaveSettings(); });
                await Check("all-filtered-widgets-remove-surface-and-release-pointer", () => { Tick(date, 30); return !Part("ClockValue").IsEffectivelyVisible && SurfaceIs(false); }, 4000);
                await Act("show-reminder-over-filtered-classroom", () => window.ShowNotificationMask("准备上课"));
                await Check("reminder-can-use-otherwise-empty-classroom", () => SurfaceIs(true) && Part("NotificationMask").IsEffectivelyVisible, 4000);
                await Act("return-to-filtered-classroom", window.HideNotificationMask);
                await Check("dismissed-reminder-leaves-no-empty-surface", () => { Tick(date, 30); return SurfaceIs(false); }, 8000);
                await Act("show-nex-over-filtered-classroom", () => window.ShowNexPanel("班级通知", false));
                await Check("nex-panel-can-use-otherwise-empty-classroom", () => SurfaceIs(true) && Part("NexPanel").IsEffectivelyVisible, 4000);
                await Act("restore-filtered-classroom-after-nex", window.HideNexPanel);
                await Check("hidden-nex-releases-empty-classroom-pointer", () => { Tick(date, 30); return SurfaceIs(false); }, 4000);
                await Act("restore-break-before-next-lesson", () => schedules.Apply(teaching));
                await Check("break-restores-previously-empty-classroom-and-input", () => { Tick(date, 41); return SurfaceIs(true) && Part("ClockValue").IsEffectivelyVisible; }, 4000);
            }
            finally
            {
                await Ui(() =>
                {
                    window.ExitEditMode(); window.HideNexPanel(); window.HideNotificationMask(); Settings.NavigateTo("widgets");
                    schedules.Remove(date); schedules.Remove(next);
                    AppServices.Settings.Widgets = original;
                    (AppServices.Settings.IsClickThrough, AppServices.Settings.HoverOpacity) = pointerPreferences;
                    AppServices.SaveSettings(); return true;
                });
            }
        }
    }
}
