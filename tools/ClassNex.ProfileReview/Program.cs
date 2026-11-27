using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassNex;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Views;
using ClassNex.Widgets;
using System.Text.Json;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Review(args); }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static int Review(string[] args)
    {
        var windows = args is [_, "--windows-offscreen"];
        if (args.Length != 1 && !windows) throw new ArgumentException("Pass a new temporary directory named classnex-profile-* containing only an app/ copy of the review build, optionally followed by --windows-offscreen.");
        if (windows) PrivateWindowReview.RequirePrivateDesktop();
        var root = new DirectoryInfo(args[0]);
        var platform = StringComparison.OrdinalIgnoreCase;
        if (!root.Name.StartsWith("classnex-profile-", StringComparison.Ordinal) ||
            !string.Equals(root.Parent?.FullName.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'), platform) ||
            !string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), Path.Combine(root.FullName, "app"), platform) ||
            !string.Equals(SettingsService.DataDirectory, Path.Combine(root.FullName, "data"), platform) ||
            Directory.Exists(SettingsService.DataDirectory) || root.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            new DirectoryInfo(AppContext.BaseDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            root.EnumerateFileSystemInfos().Any(entry => !string.Equals(entry.Name, "app", platform)))
            throw new InvalidOperationException("Refusing to read or write settings outside a new disposable review copy.");
        var today = DateTime.Today;
        var friday = today.AddDays(((int)DayOfWeek.Friday - (int)today.DayOfWeek + 7) % 7);
        var monday = friday.AddDays(3);
        var settings = new AppSettings { SingleWeekStartTime = today, IsOobeCompleted = true,
            TimeLayout = new() { Times = [new() { Name = "第一节", Start = "08:00", End = "08:45" },
                new() { Name = "大课间", Start = "08:45", End = "09:00", Kind = ClassTimeKind.Break },
                new() { Name = "眼保健操", Start = "08:55", End = "08:55", Kind = ClassTimeKind.Action }] } };
        var profile = new ScheduleProfile { Subjects = [new() { Name = "语文" }, new() { Name = "体育" }, new() { Name = "班会" }] };
        profile.GetOrCreateSchedule(TimeService.DayNumberOf(friday), "all").Classes.Add(new() { Subject = "语文" });
        profile.GetOrCreateSchedule(TimeService.DayNumberOf(monday), "all").Classes.Add(new() { Subject = "班会" });
        CsesCodec.Save(profile, SettingsService.TimetableStoragePath);
        new SettingsStore(SettingsService.SettingsPath).Save(settings);
        var original = File.ReadAllBytes(SettingsService.TimetableStoragePath);
        var builder = AppBuilder.Configure<App>().UseSkia();
        if (windows) builder.UseWin32();
        else builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        builder.SetupWithoutStarting();
        AppServices.Initialize();
        var w = new ProfileEditorWindow { Width = 1080, Height = 720, RequestedThemeVariant = ThemeVariant.Light };
        w.SelectTab(3);
        w.Show();
        void SettleWindow()
        {
            if (!windows) return;
            var frame = new DispatcherFrame();
            using var timer = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(300), DispatcherPriority.Background);
            Dispatcher.UIThread.PushFrame(frame);
        }
        SettleWindow();
        var page = Need<TemporaryScheduleEditor>(w, "PageSwap");
        var checks = new List<string>();
        var frames = new List<object>();
        void Layout() { Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); }
        void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); }
        void Shot(string name)
        {
            Layout();
            if (windows)
            {
                var mica = w.ActualTransparencyLevel == WindowTransparencyLevel.Mica;
                Check(Need<Grid>(w, "EditorShell").Classes.Contains("mica") == mica, $"{name}: shell brush matches the Windows transparency level: {w.ActualTransparencyLevel}");
                frames.Add(PrivateWindowReview.Capture(w, Path.Combine(root.FullName, name + ".png")));
                return;
            }
            using var image = w.CaptureRenderedFrame() ?? throw new InvalidOperationException("No headless frame");
            image.Save(Path.Combine(root.FullName, name + ".png"));
            frames.Add(new { File = name + ".png", Source = "Avalonia.Headless", Width = image.PixelSize.Width, Height = image.PixelSize.Height });
        }
        void Check(bool ok, string key, string? details = null)
        {
            if (!ok) throw new InvalidOperationException(details is null ? key : key + ": " + details);
            checks.Add(key);
            Console.WriteLine("PASS " + key);
        }
        Layout();
        Check(!Need<WrapPanel>(w, "BaseCommands").IsVisible && page.IsVisible, "tray swap target opens the editor without base file actions");
        var target = Need<CalendarDatePicker>(page, "TargetDate");
        var name = Need<TextBox>(page, "PlanName");
        var list = Need<ItemsControl>(page, "LessonsList");
        TextBox Field(string label, int row = 0) => list.GetVisualDescendants().OfType<TextBox>().Where(t => AutomationProperties.GetName(t) == label).ElementAt(row);
        target.SelectedDate = friday; Layout();
        Check(Field("科目").Text == "语文", "selected date starts from its actual recurring lessons");
        name.Text = "校运动会周五调课";
        Field("科目").Text = "体育";
        Field("结束时间").Text = "09:20";
        Click(Need<Button>(page, "AddLessonButton"));
        Field("科目", 1).Text = "班会";
        Layout();
        target.SelectedDate = monday;
        Layout();
        Check(Field("科目").Text == "班会", "another teaching day opens its independent base lessons");
        target.SelectedDate = friday;
        Layout();
        Check(name.Text == "校运动会周五调课" && Field("科目").Text == "体育" && Field("科目", 1).Text == "班会", "switching dates retains both sports day lessons in the unsaved draft");
        Click(Need<Button>(page, "ApplyButton"));
        var Now = () => AppServices.Time.GetTodaySummary(AppServices.Schedule.Profile, friday.AddHours(9));
        Check(Now().Current?.Subject == "体育" && Now().Next?.Subject == "班会", "sports day shows PE as the current lesson and class meeting as the next lesson");
        var saved = AppServices.Schedule.Profile.TemporarySchedules[DateOnly.FromDateTime(friday)];
        SettleWindow();
        Shot("swap-light-1080");
        Field("结束时间").Text = "07:45";
        Click(Need<Button>(page, "ApplyButton"));
        Check(Now().Current?.End == TimeSpan.FromMinutes(560) && Need<TextBlock>(page, "Feedback").Text?.Contains("结束时间必须晚于") == true,
            "invalid edit leaves the active day intact and keeps its specific validation error", Need<TextBlock>(page, "Feedback").Text);
        Click(Need<Button>(page, "DiscardButton"));
        Check(Field("结束时间").Text == "09:20", "discard restores saved schedule");
        Field("结束时间", 1).Text = "24:00";
        Click(Need<Button>(page, "AddLessonButton"));
        Check(list.Items.Count == 2 && Need<TextBlock>(page, "Feedback").Text?.Contains("当天已没有可用时段") == true,
            "a day ending at midnight cannot gain an overlapping morning class", $"rows={list.Items.Count}, feedback={Need<TextBlock>(page, "Feedback").Text}");
        Click(Need<Button>(page, "DiscardButton"));
        w.Width = 700; w.Height = 510; w.RequestedThemeVariant = ThemeVariant.Dark;
        SettleWindow();
        Layout();
        var apply = Need<Button>(page, "ApplyButton");
        var coords = apply.TranslatePoint(new Point(), w) ?? throw new InvalidOperationException("unattached footer");
        Check(coords.X >= 0 && coords.Y >= 0 && coords.X + apply.Bounds.Width <= w.Bounds.Width + 1 && coords.Y + apply.Bounds.Height <= w.Bounds.Height + 1, "narrow window retains visible apply action");
        Shot("swap-dark-700");
        var scroll = Need<ScrollViewer>(page, "DayScroll");
        scroll.ScrollToEnd(); Layout(); SettleWindow();
        var subjectBox = Field("科目");
        var rowPosition = subjectBox.TranslatePoint(default, scroll) ?? throw new InvalidOperationException("unattached course row");
        Check(scroll.Offset.Y > 0 && rowPosition.Y >= -1 && rowPosition.Y + subjectBox.Bounds.Height <= scroll.Bounds.Height + 1 &&
            coords == apply.TranslatePoint(default, w), "narrow timetable scrolls the editing row into view without moving the actions");
        Shot("swap-dark-700-scrolled");
        if (windows)
        {
            w.TransparencyLevelHint = [WindowTransparencyLevel.None]; SettleWindow(); Layout();
            var shell = Need<Grid>(w, "EditorShell");
            Check(!shell.Classes.Contains("mica") && w.ActualTransparencyLevel != WindowTransparencyLevel.Mica &&
                shell.Background is Avalonia.Media.ISolidColorBrush { Color.A: 255 }, "unavailable material falls back to an opaque theme brush");
            Shot("swap-dark-700-fallback");
        }
        var independent = ReviewDayLayout(page, w, friday, monday, Layout, Click, Shot, SettleWindow, (ok, key) => Check(ok, key));
        Click(Need<Button>(page, "RestoreButton"));
        Check(Now().Current is null && Now().Slots.Single().Subject == "语文", "withdrawal immediately restores base");
        Click(Need<Button>(page, "DiscardButton"));
        Need<Expander>(page, "CopyPanel").IsExpanded = true; Layout();
        Need<CalendarDatePicker>(page, "SourceDate").SelectedDate = monday;
        Click(page.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "复制到草稿")));
        Check(Field("科目").Text == "班会" && Now().Slots.Single().Subject == "语文", "copying from another teaching day changes only the draft");
        Click(list.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "移除")));
        Click(Need<Button>(page, "ApplyButton"));
        Check(Now().IsEmpty, "empty school day is an intentional override");
        Click(Need<Button>(page, "RestoreButton"));
        Click(Need<Button>(page, "DiscardButton"));
        w.SelectTab(-1); Layout();
        var profilePage = Need<ScrollViewer>(w, "PageProfile");
        Check(profilePage.IsVisible, "profile information remains a separate editor workspace");
        var timeLink = profilePage.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "1"));
        Click(timeLink);
        Check(Need<DockPanel>(w, "PageTime").IsVisible, "file workspace link opens time layout");
        var labels = Need<StackPanel>(w, "TimeAxisPanel").GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        Check(labels.Contains("眼保健操") && labels.Any(t => t?.Contains("大课间") == true), "activities and named breaks are present in the actual time layout");
        ReviewWorkspaceLayout(w, Layout, Click, Shot, SettleWindow, (value, name) => Check(value, name));
        Check(File.ReadAllBytes(SettingsService.TimetableStoragePath).AsSpan().SequenceEqual(original), "UI flow never rewrites the base CSES file");
        var emptyFile = Path.Combine(root.FullName, "empty.yaml");
        CsesCodec.Save(new ScheduleProfile(), emptyFile);
        var blank = new ScheduleService(() => settings, Path.Combine(root.FullName, "overrides"));
        blank.Load(emptyFile);
        var widget = new ScheduleWidget();
        var clock = friday.AddHours(9);
        void RefreshWidget() => widget.Refresh(new WidgetContext { Profile = blank.Profile, Settings = settings, Now = clock,
            Today = AppServices.Time.GetTodaySummary(blank.Profile, clock) });
        var date = DateOnly.FromDateTime(friday);
        blank.TemporarySchedules.Apply(saved);
        RefreshWidget();
        Check(widget.CurrentProgress is > 0 and < 1, "classroom widget shows the running temporary class even with no base classes or subjects");
        bool WidgetDisplays(string text) => widget.View.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);
        blank.TemporarySchedules.Apply(independent);
        clock = friday.AddHours(9).AddMinutes(21);
        RefreshWidget();
        Check(WidgetDisplays("补水休息") && widget.CurrentProgress is > 0.09 and < 0.11, "classroom widget counts down the configured break rather than guessing between lessons");
        clock = friday.AddHours(9).AddMinutes(25);
        RefreshWidget();
        Check(WidgetDisplays("接力颁奖") && widget.CurrentProgress is > 0.49 and < 0.51, "an activity takes precedence over its enclosing break in the classroom widget");
        clock = friday.AddHours(9).AddMinutes(27);
        RefreshWidget();
        Check(WidgetDisplays("补水休息") && !WidgetDisplays("检录结束") && widget.CurrentProgress is > 0.69 and < 0.71, "a point marker does not displace the resumed break");
        clock = friday.AddHours(10).AddMinutes(20);
        RefreshWidget();
        Check(WidgetDisplays("闭幕集合") && widget.CurrentProgress is > 0.49 and < 0.51, "a final activity after lessons has its own countdown");
        blank.TemporarySchedules.Apply(independent with { TimeLayout = [] });
        clock = friday.AddHours(9).AddMinutes(21);
        RefreshWidget();
        Check(!WidgetDisplays("课间休息") && widget.CurrentProgress is null, "an explicitly empty custom layout does not synthesize a break");
        blank.TemporarySchedules.Apply(saved);
        RefreshWidget();
        Check(!WidgetDisplays("课间休息") && widget.CurrentProgress is null, "withdrawing the custom layout respects unscheduled time outside the inherited base break");
        ReviewBaseClassroomWidget(friday, (value, name) => Check(value, name));
        blank.TemporarySchedules.Apply(new(date, "停课", []));
        RefreshWidget();
        Check(widget.View.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "今天没有课程"), "explicit day off is not treated as a missing timetable");
        blank.TemporarySchedules.Remove(date);
        RefreshWidget();
        Check(widget.View.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "OverlayImportHint"), "withdrawing the last temporary plan restores the empty-profile import action");
        w.Close();
        File.WriteAllText(Path.Combine(root.FullName, "review.json"), JsonSerializer.Serialize(new
        { Backend = windows ? "Windows/private desktop" : "Avalonia.Headless", Passed = true,
            Scenario = new { saved.Date, saved.Name, Lessons = saved.Lessons.Select(l => l.Subject), TimeLayout = independent.TimeLayout }, Checks = checks, Frames = frames }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static T Need<T>(Control root, string name) where T : Control =>
        root.FindControl<T>(name) ?? throw new InvalidOperationException("Missing control: " + name);
}
