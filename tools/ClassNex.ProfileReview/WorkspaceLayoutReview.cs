using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ClassNex.Views;

internal static partial class Program
{
    private static void ReviewWorkspaceLayout(ProfileEditorWindow window, Action layout, Action<Button> click,
        Action<string> shot, Action settle, Action<bool, string> check)
    {
        void PerformLayout() { layout(); settle(); layout(); }
        Control ControlOf(string name) => Need<Control>(window, name);
        void Show(int page) { window.SelectTab(page); PerformLayout(); }
        bool Inside(Control control, Visual parent)
        {
            if (control.TranslatePoint(default, parent) is not { } point) return false;
            return control.IsEffectivelyVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0 &&
                point.X >= -2 && point.Y >= -2 && point.X + control.Bounds.Width <= parent.Bounds.Width + 2 &&
                point.Y + control.Bounds.Height <= parent.Bounds.Height + 2;
        }
        static Point Location(Visual view, Visual ancestor) => view.TranslatePoint(default, ancestor) ?? throw new InvalidOperationException("Missing layout root");
        void CheckTimelineContrast(string stage)
        {
            var panel = Need<StackPanel>(window, "TimeAxisPanel");
            var labels = panel.GetVisualDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible).ToArray();
            if (!window.TryFindResource("ClassNexCanvasBrush", window.ActualThemeVariant, out var surface) || surface is not ISolidColorBrush canvas)
                throw new InvalidOperationException("The editor canvas has no theme brush");
            foreach (var label in labels)
            {
                var entry = label.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("timelineItem"));
                if (label.Foreground is not ISolidColorBrush ink || (entry is not null && entry.Background is not ISolidColorBrush))
                    throw new InvalidOperationException($"Unresolved {stage} colors for '{label.Text}'");
                var paper = entry?.Background is ISolidColorBrush tint ? Composite(tint, canvas.Color) : canvas.Color;
                var printed = Composite(ink, paper, label.Opacity);
                var contrast = (Math.Max(Luminance(printed), Luminance(paper)) + .05) / (Math.Min(Luminance(printed), Luminance(paper)) + .05);
                if (contrast < 4.5) throw new InvalidOperationException($"{stage} timeline text '{label.Text}' has contrast {contrast:0.00}:1");
            }
            check(labels.Any(label => label.Text?.StartsWith("大课间", StringComparison.Ordinal) == true) && labels.Any(label => label.Text == "眼保健操"),
                $"{stage} timeline times, lessons, breaks and activities retain at least 4.5:1 contrast against the resolved canvas brushes");
        }
        static Color Composite(ISolidColorBrush ink, Color background, double opacity = 1)
        {
            var alpha = ink.Color.A / 255.0 * ink.Opacity * opacity;
            byte Mix(byte front, byte back) => (byte)Math.Round(front * alpha + back * (1 - alpha));
            return Color.FromRgb(Mix(ink.Color.R, background.R), Mix(ink.Color.G, background.G), Mix(ink.Color.B, background.B));
        }
        static double Luminance(Color color)
        {
            static double Linear(byte component)
            {
                var channel = component / 255.0;
                return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4);
            }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }

        window.Width = 1080; window.Height = 700; window.RequestedThemeVariant = ThemeVariant.Light;
        Show(1);
        CheckTimelineContrast("initial light");
        Show(0);
        var week = Need<DataGrid>(window, "WeekGrid");
        bool WeekLabelsFit()
        {
            var headers = week.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is StackPanel).ToArray();
            if (headers.Length != 7) throw new InvalidOperationException($"Expected seven day headers, found {headers.Length}");
            var days = new HashSet<string>(["周日", "周一", "周二", "周三", "周四", "周五", "周六"]);
            foreach (var header in headers)
            {
                if (header.GetVisualDescendants().OfType<TextBlock>().ToArray() is not [var weekday, var date] ||
                    !days.Remove(weekday.Text ?? "") || date.Text is not { Length: 5 })
                    throw new InvalidOperationException($"Unexpected rendered header: {string.Join(" / ", header.GetVisualDescendants().OfType<TextBlock>().Select(label => label.Text))}");
                foreach (var label in new[] { weekday, date })
                    if (!Inside(label, header) || !Inside(label, week) || label.TextLayout.TextLines.Count != 1 ||
                        label.TextLayout.TextLines[0] is not { HasCollapsed: false } line || line.Width > label.Bounds.Width + 1)
                        throw new InvalidOperationException($"Clipped '{label.Text}' at {Location(label, header)}, label {label.Bounds}, header width {header.Bounds.Width}, padding {header.Padding}, lines: " +
                            string.Join(", ", label.TextLayout.TextLines.Select(l => $"{l.Width:0.#} DIP, ellipsis = {l.HasCollapsed}")));
            }
            return days.Count == 0;
        }
        var workspace = ControlOf("WeekWorkspace");
        var palette = ControlOf("WeekInspector");
        check(week.Columns.Count == 8 && week.Columns.Skip(1).All(c => c.ActualWidth >= 59) && Inside(week, workspace) && Inside(palette, workspace) &&
            Location(palette, workspace).X >= week.Bounds.Width, "wide weekly editor shows all seven days and the subject bank side-by-side");
        var date = Need<TextBlock>(window, "NavDateText");
        var previous = date.Text;
        click(Need<Button>(window, "NextWeekButton"));
        check(date.Text != previous, "forward week navigation updates dates");
        click(Need<Button>(window, "PrevWeekButton"));
        check(date.Text == previous, "backward week navigation restores dates");
        shot("workspace-week-light-1080");
        check(WeekLabelsFit(), "wide weekly column headers draw all weekday and date text without ellipsis");

        window.Width = 700; window.Height = 510; window.RequestedThemeVariant = ThemeVariant.Dark;
        PerformLayout();
        check(Inside(week, workspace) && Inside(palette, workspace) && Location(palette, workspace).Y >= week.Bounds.Height &&
            Math.Abs(week.Bounds.Width - palette.Bounds.Width) < 2 && !ControlOf("WeekSplitter").IsVisible,
            "narrow weekly editor puts the subject bank below the seven-day grid");
        shot("workspace-week-dark-700");
        check(WeekLabelsFit(), "narrow weekly column headers draw all weekday and date text without ellipsis");

        Show(1);
        var time = ControlOf("TimeWorkspace");
        var axis = ControlOf("TimeAxisScroll");
        var timeInspector = ControlOf("TimeInspector");
        var add = Need<Button>(window, "AddTimeButton");
        check(Inside(add, ControlOf("TimeToolbar")) && Inside(ControlOf("TimeOptionsButton"), ControlOf("TimeToolbar")) &&
            add.Flyout is MenuFlyout { Items.Count: 4 } && !ControlOf("MoveUpButton").IsEnabled,
            "all time-entry types are available in a reachable menu without clipping the toolbar");
        check(Inside(axis, time) && Inside(timeInspector, time) && Location(timeInspector, time).Y >= axis.Bounds.Height,
            "narrow time layout keeps a separate scrollable inspector below the timeline");
        check(ControlOf("TimeFieldHint").IsEffectivelyVisible && !ControlOf("TimeFields").IsEffectivelyVisible &&
            !ControlOf("TimeStartPicker").IsEffectivelyVisible, "an unselected time entry shows a Chinese hint instead of empty time fields");
        CheckTimelineContrast("switched dark");
        shot("workspace-time-dark-700");

        Show(2);
        var subject = ControlOf("PageSubject");
        var subjectInspector = ControlOf("SubjectInspector");
        var subjectList = ControlOf("SubjectListPane");
        var scroll = Need<ScrollViewer>(window, "SubjectInspector");
        check(Inside(subjectList, subject) && Inside(subjectInspector, subject) && Location(subjectInspector, subject).Y >= subjectList.Bounds.Height &&
            !ControlOf("SubjectFields").IsEnabled && !ControlOf("DeleteSubjectButton").IsEnabled,
            "narrow subject editor keeps the list and fields visible, with actions disabled before selection");
        scroll.ScrollToEnd(); PerformLayout();
        check(scroll.Offset.Y > 0 && Inside(ControlOf("SaveSubjectsButton"), scroll), "narrow subject form can be scrolled to its final action");
        shot("workspace-subject-dark-700");

        window.Width = 1080; window.Height = 700; window.RequestedThemeVariant = ThemeVariant.Light;
        Show(1);
        check(Inside(axis, time) && Inside(timeInspector, time) && Location(timeInspector, time).X >= axis.Bounds.Width,
            "growing the window restores the side-by-side work area");
        var firstTimeBlock = Need<StackPanel>(window, "TimeAxisPanel").Children.OfType<Grid>().First().Children.OfType<Border>().Single();
        firstTimeBlock.RaiseEvent(new PointerPressedEventArgs(firstTimeBlock, new Pointer(1, PointerType.Mouse, true), window,
            Location(firstTimeBlock, window), 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
        PerformLayout();
        var start = Need<TimePicker>(window, "TimeStartPicker");
        var end = Need<TimePicker>(window, "TimeEndPicker");
        check(!ControlOf("TimeFieldHint").IsEffectivelyVisible && Inside(start, timeInspector) && Inside(end, timeInspector) &&
            start.ClockIdentifier == "24HourClock" && end.ClockIdentifier == "24HourClock" &&
            start.SelectedTime == TimeSpan.FromHours(8) && end.SelectedTime == TimeSpan.FromMinutes(525),
            "selecting a real timeline row opens reachable 24-hour fields with its existing time values");
        CheckTimelineContrast("restored light");
        shot("workspace-time-light-1080");
    }
}
