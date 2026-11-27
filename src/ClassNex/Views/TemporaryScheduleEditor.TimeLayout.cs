using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassNex.Models;

namespace ClassNex.Views;

public partial class TemporaryScheduleEditor
{
    private bool _loadingLayout;

    private static TimeRow TimeRowFor(TemporaryTimeItem item) => new(item.Kind)
    { Name = item.Name, Start = Format(item.Start), End = Format(item.End) };

    private void LoadTimeRows(TimeRow[]? rows)
    {
        _loadingLayout = true;
        try
        {
            UseDayLayout.IsChecked = rows is not null;
            CustomLayoutBody.IsVisible = rows is not null;
            LayoutHint.Text = rows is null ? "沿用基础时间表。" : "课间和活动仅对当天生效，关闭后恢复基础作息。定点活动的起止时间相同。";
            _timeRows.Clear();
            if (rows is not null) foreach (var row in rows) _timeRows.Add(row with { });
        }
        finally { _loadingLayout = false; }
    }

    private static TemporaryTimeItem ReadTimeRow(TimeRow row)
    {
        if (!TryClock(row.Start, out var start) || !TryClock(row.IsMarker ? row.Start : row.End, out var end))
            throw new ArgumentException($"请为{row.KindLabel}“{row.Name}”填写有效的 24 小时制时间，例如 09:20。");
        return new(row.Kind, row.Name, start, end);
    }

    private void OnAddTimeItem(object? sender, RoutedEventArgs e)
    {
        if (_date is null || UseDayLayout.IsChecked != true || sender is not Button { Tag: string tag } ||
            !Enum.TryParse<ClassTimeKind>(tag, out var kind)) return;
        var courses = new List<(TimeSpan Start, TimeSpan End)>();
        foreach (var row in _rows)
            if (TryClock(row.Start, out var start) && TryClock(row.End, out var end) && end > start) courses.Add((start, end));
        var cursor = _timeRows.Count > 0 ? TimeSpan.Zero : courses.Count == 0 ? TimeSpan.FromHours(8) : courses.Min(c => c.End);
        foreach (var row in _timeRows)
        {
            if (!TryClock(row.IsMarker ? row.Start : row.End, out var edge)) { ShowFeedback("请先补全已有作息的时间。"); return; }
            if (edge > cursor) cursor = edge;
        }
        var dayEnd = TimeSpan.FromDays(1);
        var limit = dayEnd;
        if (kind != ClassTimeKind.Separator)
            foreach (var course in courses.OrderBy(c => c.Start))
            {
                if (course.End <= cursor) continue;
                if (course.Start > cursor) { limit = course.Start; break; }
                cursor = course.End;
            }
        if (cursor >= dayEnd) { ShowFeedback("当天已没有可用时段，请先调整已有作息。"); return; }
        var endTime = kind == ClassTimeKind.Separator ? cursor : TimeSpan.FromTicks(Math.Min(limit.Ticks, (cursor + TimeSpan.FromMinutes(10)).Ticks));
        var name = kind switch { ClassTimeKind.Break => "课间", ClassTimeKind.Action => "活动", _ => "分隔点" };
        _timeRows.Add(TimeRowFor(new(kind, name, cursor, endTime)));
        ShowFeedback("已添加到当天作息，应用后生效。");
    }

    private void OnRemoveTimeItem(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TimeRow row }) return;
        for (var i = 0; i < _timeRows.Count; i++)
            if (ReferenceEquals(_timeRows[i], row)) { _timeRows.RemoveAt(i); break; }
        ShowFeedback("已从当天作息移除，应用后生效。");
    }
}
