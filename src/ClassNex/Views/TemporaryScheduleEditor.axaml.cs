using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Views;

public partial class TemporaryScheduleEditor : UserControl
{
    private sealed record LessonRow
    {
        public string Subject { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
    }

    private sealed record TimeRow(ClassTimeKind Kind)
    {
        public string Name { get; set; } = "";
        public string Start { get; set; } = "";
        public string End { get; set; } = "";
        public string KindLabel => Kind switch { ClassTimeKind.Break => "课间", ClassTimeKind.Action => "活动", _ => "分隔" };
        public bool IsMarker => Kind == ClassTimeKind.Separator;
        public bool HasEnd => !IsMarker;
    }

    private sealed record Draft(string Name, LessonRow[] Rows, TimeRow[]? Layout = null);
    private sealed record PlannedDate(DateOnly Date, string Name)
    {
        public override string ToString() => $"{Date:MM/dd}　{Name}";
    }

    private readonly ObservableCollection<LessonRow> _rows = [];
    private readonly ObservableCollection<TimeRow> _timeRows = [];
    private readonly Dictionary<DateOnly, Draft> _drafts = [];
    private ScheduleProfile? _profile;
    private ITimeService? _time;
    private ITimeLayoutService? _baseLayout;
    private TemporaryScheduleStore? _store;
    private Func<DateTime> _now = () => DateTime.Now;
    private string? _file;
    private DateOnly? _date;
    private Draft? _original, _feedbackDraft;
    private bool _syncing;
    private const string PendingNotice = "草稿尚未应用。";

    public TemporaryScheduleEditor()
    {
        InitializeComponent();
        LessonsList.ItemsSource = _rows;
        DayTimeItems.ItemsSource = _timeRows;
        UseDayLayout.IsCheckedChanged += (_, _) =>
        {
            if (_syncing || _loadingLayout) return;
            LoadTimeRows(UseDayLayout.IsChecked == true ? TemporaryTimeItem.FromBase(_baseLayout!.Layout)
                .Where(t => t.Kind is ClassTimeKind.Break or ClassTimeKind.Action or ClassTimeKind.Separator).Select(TimeRowFor).ToArray() : null);
            ShowFeedback(IsEdited ? PendingNotice : null);
        };
        TargetDate.SelectedDateChanged += (_, _) =>
        {
            if (_syncing || _profile is null) return;
            RememberDraft();
            ShowDate(TargetDate.SelectedDate is { } selected ? DateOnly.FromDateTime(selected) : null);
        };
        Upcoming.SelectionChanged += (_, _) =>
        {
            if (!_syncing && Upcoming.SelectedItem is PlannedDate plan)
                TargetDate.SelectedDate = plan.Date.ToDateTime(TimeOnly.MinValue);
        };
        AddHandler(TextBox.TextChangedEvent, (_, _) =>
        {
            if (_syncing || _date is null || _feedbackDraft is { } shown && Matches(shown)) return;
            var text = IsEdited ? PendingNotice : null;
            if (Feedback.Text != text) ShowFeedback(text);
        });
    }

    private bool Matches(Draft draft) => draft.Name == PlanName.Text && _rows.SequenceEqual(draft.Rows) &&
        (UseDayLayout.IsChecked == true ? draft.Layout is not null && _timeRows.SequenceEqual(draft.Layout) : draft.Layout is null);
    private bool IsEdited => _original is { } draft && !Matches(draft);
    internal bool HasPendingChanges => IsEdited || _drafts.Keys.Any(d => d != _date);

    internal void ForgetDrafts()
    {
        _drafts.Clear();
        _original = null;
    }

    internal void Bind(IScheduleService schedule, TemporaryScheduleStore store, ITimeService time, ITimeLayoutService baseLayout, Func<DateTime> now)
    {
        var sameFile = string.Equals(_file, schedule.FilePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (sameFile) RememberDraft(); else _drafts.Clear();
        (_file, _profile, _store, _time, _baseLayout, _now) = (schedule.FilePath, schedule.Profile, store, time, baseLayout, now);
        var today = DateOnly.FromDateTime(now());
        var date = sameFile && _date >= today ? _date.Value : today;
        _syncing = true;
        TargetDate.DisplayDateStart = today.ToDateTime(TimeOnly.MinValue);
        TargetDate.SelectedDate = date.ToDateTime(TimeOnly.MinValue);
        _syncing = false;
        ShowDate(date);
    }

    private Draft Snapshot() => new(PlanName.Text ?? "", _rows.Select(row => row with { }).ToArray(),
        UseDayLayout.IsChecked == true ? _timeRows.Select(row => row with { }).ToArray() : null);

    private void RememberDraft()
    {
        if (_date is not { } date) return;
        if (IsEdited) _drafts[date] = Snapshot(); else _drafts.Remove(date);
    }

    private void ShowDate(DateOnly? date)
    {
        _syncing = true;
        try
        {
            _date = date;
            _original = null;
            _rows.Clear();
            LoadTimeRows(null);
            PlanName.Text = "";
            DayScroll.Offset = Vector.Zero;
            ShowFeedback(null);
            var today = DateOnly.FromDateTime(_now());
            var plans = _profile!.TemporarySchedules.Where(x => x.Key >= today)
                .Select(x => new PlannedDate(x.Key, x.Value.Name)).ToArray();
            Upcoming.ItemsSource = plans;
            Upcoming.SelectedItem = plans.FirstOrDefault(x => x.Date == date);
            Upcoming.PlaceholderText = plans.Length == 0 ? "暂无安排" : "选择日期…";
            Upcoming.IsEnabled = plans.Length > 0;
            var valid = date is not null && date >= today && _store!.LoadError is null;
            ApplyButton.IsEnabled = DiscardButton.IsEnabled = CopyPanel.IsEnabled = valid;
            DayLayoutPanel.IsEnabled = valid;
            PlanName.IsEnabled = LessonsList.IsEnabled = AddLessonButton.IsEnabled = valid;
            RestoreButton.IsEnabled = false;
            if (date is not { } day || day < today)
            {
                DayStatus.Text = "请选择今天或之后的日期。";
                EmptyHint.IsVisible = false;
                return;
            }
            var saved = _profile.TemporarySchedules.GetValueOrDefault(day);
            RestoreButton.IsEnabled = valid && saved is not null;
            _original = saved is null
                ? new("临时课表", BaseRows(day))
                : new(saved.Name, saved.Lessons.Select(l => Row(l.Subject, l.Start, l.End)).ToArray(),
                    saved.TimeLayout?.Select(TimeRowFor).ToArray());
            LoadDraft(_drafts.GetValueOrDefault(day) ?? _original);
            DayStatus.Text = _store!.LoadError ?? (saved is null ? "当前安排：基础课表" :
                day == today ? $"正在使用：{saved.Name}" : $"届时使用：{saved.Name}");
            SourceDate.SelectedDate = day.ToDateTime(TimeOnly.MinValue);
            if (IsEdited) ShowFeedback(PendingNotice);
        }
        finally { _syncing = false; }
    }

    private LessonRow[] BaseRows(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        return _time!.GetDaySlots(_profile!, TimeService.DayNumberOf(local), _time.GetCurrentParity(local))
            .Select(l => Row(l.Subject, l.Start, l.End)).ToArray();
    }

    private static LessonRow Row(string subject, TimeSpan start, TimeSpan end) => new()
    {
        Subject = subject, Start = Format(start), End = Format(end)
    };

    private static string Format(TimeSpan value) => value == TimeSpan.FromDays(1) ? "24:00" :
        TimeOnly.FromTimeSpan(value).ToString(value.Ticks % TimeSpan.TicksPerSecond != 0 ? "HH:mm:ss.FFFFFFF" : value.Seconds != 0 ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

    private static bool TryClock(string? value, out TimeSpan clock) => TemporaryTimeItem.TryClock(value, out clock);

    private static TimeSpan Parse(string value, int row) => TryClock(value, out var clock) ? clock :
        throw new ArgumentException($"第 {row} 节的时间“{value}”无效，请使用 24 小时制，例如 08:45。");

    private void LoadDraft(Draft draft)
    {
        PlanName.Text = draft.Name;
        _rows.Clear();
        foreach (var row in draft.Rows) _rows.Add(row with { });
        LoadTimeRows(draft.Layout);
        EmptyHint.IsVisible = _rows.Count == 0;
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (SourceDate.SelectedDate is not { } source || _date is null) return;
        LoadDraft(Snapshot() with { Rows = BaseRows(DateOnly.FromDateTime(source)) });
        ShowFeedback($"已复制 {source:M月d日} 的基础课程到草稿。应用后生效。");
    }

    private void OnAddLesson(object? sender, RoutedEventArgs e)
    {
        if (_date is null) return;
        var start = TimeSpan.FromHours(8);
        if (_rows.LastOrDefault() is { } last)
        {
            if (!TryClock(last.End, out var end)) { ShowFeedback("请先填写上一节的有效结束时间。"); return; }
            start = end + TimeSpan.FromMinutes(10);
        }
        if (start >= TimeSpan.FromDays(1)) { ShowFeedback("当天已没有可用时段，请先调整上一节的结束时间。"); return; }
        _rows.Add(Row("", start, TimeSpan.FromTicks(Math.Min(TimeSpan.TicksPerDay, start.Ticks + TimeSpan.FromMinutes(45).Ticks))));
        EmptyHint.IsVisible = false;
        ShowFeedback("请填写新一节课的科目和时间。");
    }

    private void OnRemoveLesson(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LessonRow row }) return;
        for (var i = 0; i < _rows.Count; i++)
            if (ReferenceEquals(_rows[i], row)) { _rows.RemoveAt(i); break; }
        EmptyHint.IsVisible = _rows.Count == 0;
        ShowFeedback("已从草稿移除，应用后生效。");
    }

    private void OnDiscard(object? sender, RoutedEventArgs e)
    {
        if (_date is not { } day) return;
        _drafts.Remove(day);
        ShowDate(day);
        ShowFeedback("已放弃这一天未应用的修改。");
    }

    private void OnApply(object? sender, RoutedEventArgs e) => Apply();

    internal void Apply()
    {
        if (_date is not { } date || _store is null || !ApplyButton.IsEnabled) return;
        try
        {
            var lessons = _rows.Select((r, i) => new TemporaryLesson(r.Subject, Parse(r.Start, i + 1), Parse(r.End, i + 1))).ToImmutableArray();
            _store.Apply(new(date, PlanName.Text ?? "", lessons) { TimeLayout = UseDayLayout.IsChecked == true ? _timeRows.Select(ReadTimeRow).ToImmutableArray() : null });
            _drafts.Remove(date);
            ShowDate(date);
            ShowFeedback(date == DateOnly.FromDateTime(_now()) ? "已应用到今天的课堂。" : $"已安排，将于 {date:M月d日} 自动生效。");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { ShowFeedback($"未应用：{error.Message}"); }
    }

    private void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (_date is not { } date || _store is null) return;
        try
        {
            var draft = Snapshot();
            if (!_store.Remove(date)) return;
            _drafts[date] = draft;
            ShowDate(date);
            ShowFeedback("已恢复当天的基础课表。撤下的安排仍在草稿中，可重新应用或放弃。");
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        { ShowFeedback($"未撤销：{error.Message}"); }
    }

    private void ShowFeedback(string? text)
    {
        _feedbackDraft = text is null or PendingNotice ? null : Snapshot();
        Feedback.Text = text;
        Feedback.IsVisible = !string.IsNullOrEmpty(text);
    }
}
