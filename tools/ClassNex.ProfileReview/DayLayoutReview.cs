using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Views;

internal static partial class Program
{
    private static TemporarySchedule ReviewDayLayout(TemporaryScheduleEditor page, ProfileEditorWindow window, DateTime friday, DateTime monday,
        Action layout, Action<Button> click, Action<string> shot, Action settle, Action<bool, string> check)
    {
        var panel = Need<Expander>(page, "DayLayoutPanel");
        var toggle = Need<ToggleSwitch>(page, "UseDayLayout");
        var list = Need<ItemsControl>(page, "DayTimeItems");
        var scroll = Need<ScrollViewer>(page, "DayScroll");
        var date = Need<CalendarDatePicker>(page, "TargetDate");
        var apply = Need<Button>(page, "ApplyButton");
        TextBox Field(string name, int index) => list.GetVisualDescendants().OfType<TextBox>().Where(t => AutomationProperties.GetName(t) == name).ElementAt(index);
        void Row(int row, string name, string start, string end)
        {
            Field("作息名称", row).Text = name;
            Field("作息开始", row).Text = start;
            Field("作息结束", row).Text = end;
            layout();
        }
        void ShowLayout()
        {
            layout(); settle(); layout();
            var y = panel.TranslatePoint(default, scroll)!.Value.Y;
            scroll.Offset = new Vector(0, Math.Max(0, scroll.Offset.Y + y - 6));
            layout(); settle(); layout();
        }
        window.Width = 1080; window.Height = 780; window.RequestedThemeVariant = ThemeVariant.Light;
        window.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
        panel.IsExpanded = true; layout(); settle();
        check(toggle.IsChecked == false, "an existing lessons-only plan inherits the base layout by default");
        toggle.IsChecked = true; layout(); settle();
        check(list.Items.Count == 2 && Field("作息名称", 0).Text == "大课间", "independent layout starts with isolated copies of non-lesson base entries");
        click(apply);
        check(Need<TextBlock>(page, "Feedback").Text?.Contains("时间重叠") == true && AppServices.Schedule.Profile.TemporarySchedules[DateOnly.FromDateTime(friday)].TimeLayout is null,
            "a copied break crossing a rescheduled lesson is rejected without replacing the active plan");
        Row(0, "补水休息", "09:20", "09:30");
        Row(1, "接力颁奖", "09:23", "09:27");
        click(Need<Button>(page, "AddSeparatorButton"));
        Field("作息名称", 2).Text = "检录结束"; Field("作息开始", 2).Text = "09:27"; layout();
        click(Need<Button>(page, "AddActionButton"));
        Row(3, "闭幕集合", "10:15", "10:25");
        click(Need<Button>(page, "AddBreakButton"));
        click(list.GetVisualDescendants().OfType<Button>().Last(b => AutomationProperties.GetName(b) == "移除当天作息项"));
        check(list.Items.Count == 4, "adding and removing a day-only break changes the draft in place");
        date.SelectedDate = monday; layout();
        check(toggle.IsChecked == false, "the following teaching day does not inherit the sports day's draft");
        date.SelectedDate = friday; layout();
        check(toggle.IsChecked == true && list.Items.Count == 4 && Field("作息名称", 3).Text == "闭幕集合", "switching dates preserves the independent layout draft");
        click(apply);
        var saved = AppServices.Schedule.Profile.TemporarySchedules[DateOnly.FromDateTime(friday)];
        using var day = new DayScheduleCache(AppServices.Time, AppServices.Schedule);
        var summary = day.Read(friday.AddMinutes(565));
        check(saved.TimeLayout is { Length: 4 } && summary.GetActiveTimeItem(TimeSpan.FromMinutes(565))?.Name == "接力颁奖" &&
            summary.GetActiveTimeItem(TimeSpan.FromMinutes(567))?.Name == "补水休息", "the live classroom snapshot uses actual activity and break boundaries");
        ShowLayout(); shot("day-layout-light");
        window.Width = 700; window.Height = 620; window.RequestedThemeVariant = ThemeVariant.Dark;
        ShowLayout(); shot("day-layout-dark-700");
        var first = Field("作息名称", 0).TranslatePoint(default, scroll)!.Value;
        var last = Field("作息名称", 3).TranslatePoint(default, scroll)!.Value;
        check(first.Y >= 0 && last.Y + Field("作息名称", 3).Bounds.Height <= scroll.Bounds.Height && apply.TranslatePoint(default, window)!.Value.Y < window.Bounds.Height,
            "all four time rows and the fixed apply action fit the narrow editor");
        toggle.IsChecked = false; layout(); click(apply);
        summary = day.Read(friday.AddHours(8));
        check(summary.IndependentTimeLayout is null && summary.Current?.Subject == "体育" &&
            summary.ResolveTimeLayout(AppServices.TimeLayout.Layout).Any(t => t.Name == "大课间"), "restoring base times leaves the Friday lesson changes intact");
        check(AppServices.TimeLayout.Layout.Times.Count == 3 && AppServices.TimeLayout.Layout.Times[2].Name == "眼保健操", "base settings and marker identities are unchanged");
        panel.IsExpanded = false; layout();
        return saved;
    }
}
